using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Automaton.Api.Security;

/// <summary>Decodes Traefik v3.3 PassTLSClientCert.pem=true leaf header values.</summary>
public static class NodeCertificateHeaderDecoder
{
    public const int MaximumEncodedLength = 8192;

    public static bool TryDecode(IHeaderDictionary headers, out DecodedNodeCertificate? certificate)
    {
        certificate = null;
        if (!headers.TryGetValue("X-Forwarded-Tls-Client-Cert", out var values) || values.Count != 1)
            return false;

        return TryDecode(values[0], out certificate);
    }

    public static bool TryDecode(string? encoded, out DecodedNodeCertificate? certificate)
    {
        certificate = null;
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumEncodedLength || encoded.Length % 4 != 0)
            return false;

        foreach (var character in encoded)
        {
            if (!IsBase64Character(character))
                return false;
        }

        byte[] der;
        try
        {
            der = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!string.Equals(Convert.ToBase64String(der), encoded, StringComparison.Ordinal) || !IsOneDerCertificate(der))
            return false;

        try
        {
            var x509 = X509CertificateLoader.LoadCertificate(der);
            certificate = new DecodedNodeCertificate(x509, SHA256.HashData(der));
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool IsBase64Character(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=';

    private static bool IsOneDerCertificate(ReadOnlyMemory<byte> der)
    {
        try
        {
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            var certificate = reader.ReadSequence();
            certificate.ReadEncodedValue();
            certificate.ReadEncodedValue();
            certificate.ReadEncodedValue();
            return !certificate.HasData && !reader.HasData;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }
}

public sealed record DecodedNodeCertificate(X509Certificate2 Certificate, byte[] Sha256DerFingerprint);
