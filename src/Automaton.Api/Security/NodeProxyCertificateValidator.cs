using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Automaton.Api.Config;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Security;

/// <summary>Authenticates the TLS peer before accepting Traefik-forwarded Node certificate data.</summary>
public sealed class NodeProxyCertificateValidator(IOptionsMonitor<NodeProxyAuthenticationOptions> options)
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    private const string SubjectAlternativeNameOid = "2.5.29.17";
    private const string EnhancedKeyUsageOid = "2.5.29.37";
    private const int MaximumRootCertificateBase64Length = 16 * 1024;

    public bool IsTrustedPeer(X509Certificate2? peerCertificate, DateTime utcNow)
    {
        var current = options.CurrentValue;
        if (peerCertificate is null || !IsAllowedLeafFingerprint(peerCertificate, current.AllowedLeafSha256Fingerprints) ||
            !HasClientAuthenticationEku(peerCertificate) ||
            !HasRequiredUriSan(peerCertificate))
            return false;

        if (!TryLoadTrustedRoot(current.TrustedRootCertificateBase64, out var root))
            return false;

        using (root)
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ClientAuthenticationOid));
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = utcNow.ToUniversalTime();
            chain.ChainPolicy.DisableCertificateDownloads = true;
            return chain.Build(peerCertificate);
        }
    }

    private static bool IsAllowedLeafFingerprint(X509Certificate2 certificate, IEnumerable<string>? allowedFingerprints)
    {
        if (allowedFingerprints is null)
            return false;

        var actual = SHA256.HashData(certificate.RawData);
        foreach (var configured in allowedFingerprints)
        {
            try
            {
                var expected = Convert.FromHexString(configured);
                if (expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(actual, expected))
                    return true;
            }
            catch (FormatException)
            {
                continue;
            }
        }

        return false;
    }

    private static bool HasClientAuthenticationEku(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();
        return extension?.EnhancedKeyUsages
            .Cast<Oid>()
            .Any(oid => string.Equals(oid.Value, ClientAuthenticationOid, StringComparison.Ordinal)) == true;
    }

    private static bool HasRequiredUriSan(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions.SingleOrDefault(value => value.Oid?.Value == SubjectAlternativeNameOid);
        if (extension is null)
            return false;

        try
        {
            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            var names = reader.ReadSequence();
            var uriTag = new Asn1Tag(TagClass.ContextSpecific, 6);
            var foundRequiredUri = false;
            while (names.HasData)
            {
                var tag = names.PeekTag();
                if (tag.HasSameClassAndValue(uriTag) &&
                    string.Equals(
                        names.ReadCharacterString(UniversalTagNumber.IA5String, uriTag),
                        NodeProxyAuthenticationOptions.RequiredUriSan,
                        StringComparison.Ordinal))
                {
                    foundRequiredUri = true;
                    continue;
                }

                names.ReadEncodedValue();
            }

            return foundRequiredUri && !reader.HasData;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static bool TryLoadTrustedRoot(string encoded, out X509Certificate2? root)
    {
        root = null;
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumRootCertificateBase64Length ||
            encoded.Any(char.IsWhiteSpace))
            return false;

        try
        {
            var der = Convert.FromBase64String(encoded);
            if (!string.Equals(Convert.ToBase64String(der), encoded, StringComparison.Ordinal))
                return false;

            var certificate = X509CertificateLoader.LoadCertificate(der);
            if (certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault()?.CertificateAuthority != true)
            {
                certificate.Dispose();
                return false;
            }

            root = certificate;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
