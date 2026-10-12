using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Automaton.Api.Security;

internal static class NodeEnrollmentCryptography
{
    private const string EcdsaSha256Oid = "1.2.840.10045.4.3.2";
    private const string RecoveryDomain = "Automaton.Node.EnrollmentRecovery.v1";

    public static string HashUtf8(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool MatchesSha256Hex(string actual, string expected)
    {
        try
        {
            var actualBytes = Convert.FromHexString(actual);
            var expectedBytes = Convert.FromHexString(expected);
            return actualBytes.Length == 32 && expectedBytes.Length == 32 &&
                   CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool ValidateEnrollmentProof(
        ReadOnlyMemory<byte> csrDer,
        ReadOnlyMemory<byte> certificateDer,
        string pinnedPublicKeySha256,
        DateTime utcNow,
        out X509Certificate2? certificate)
    {
        certificate = null;
        if (csrDer.Length is 0 or > 16 * 1024 || certificateDer.Length is 0 or > 16 * 1024 ||
            !TryReadCsr(csrDer, out var requestInfo, out var csrPublicKey, out var csrSignature) ||
            !TryReadCertificate(certificateDer, out var tbs, out var certificatePublicKey, out var certificateSignature))
            return false;

        using var publicKey = ECDsa.Create();
        try
        {
            publicKey.ImportSubjectPublicKeyInfo(csrPublicKey, out var csrConsumed);
            if (csrConsumed != csrPublicKey.Length ||
                !publicKey.VerifyData(requestInfo.Span, csrSignature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence) ||
                !certificatePublicKey.AsSpan().SequenceEqual(csrPublicKey) ||
                !MatchesSha256Hex(Convert.ToHexString(SHA256.HashData(csrPublicKey)), pinnedPublicKeySha256) ||
                !publicKey.VerifyData(tbs.Span, certificateSignature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                return false;

            var parsed = new X509Certificate2(certificateDer.Span);
            var notBefore = parsed.NotBefore.ToUniversalTime();
            var notAfter = parsed.NotAfter.ToUniversalTime();
            if (notBefore > utcNow || notAfter <= utcNow || notAfter - notBefore > TimeSpan.FromDays(90) ||
                !string.Equals(parsed.Subject, parsed.Issuer, StringComparison.Ordinal))
            {
                parsed.Dispose();
                return false;
            }

            certificate = parsed;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static byte[] BuildRecoveryMessage(
        Guid challengeId,
        ReadOnlySpan<byte> nonce,
        Guid enrollmentId,
        Guid installationId,
        Guid realmId)
    {
        var nonceBase64Url = Convert.ToBase64String(nonce).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var message = string.Join('\n',
            RecoveryDomain,
            challengeId.ToString("D").ToLowerInvariant(),
            nonceBase64Url,
            enrollmentId.ToString("D").ToLowerInvariant(),
            installationId.ToString("D").ToLowerInvariant(),
            realmId.ToString("D").ToLowerInvariant(),
            "RecoverCommittedEnrollmentResponse",
            string.Empty);
        return Encoding.UTF8.GetBytes(message);
    }

    public static bool VerifyRecoveryProof(
        ReadOnlyMemory<byte> certificateDer,
        ReadOnlySpan<byte> message,
        ReadOnlySpan<byte> signature)
    {
        try
        {
            using var certificate = new X509Certificate2(certificateDer.Span);
            using var key = certificate.GetECDsaPublicKey();
            return key is not null && key.VerifyData(
                message,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryReadCsr(
        ReadOnlyMemory<byte> der,
        out ReadOnlyMemory<byte> requestInfo,
        out byte[] publicKey,
        out byte[] signature)
    {
        requestInfo = default;
        publicKey = Array.Empty<byte>();
        signature = Array.Empty<byte>();
        try
        {
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            var csr = reader.ReadSequence();
            requestInfo = csr.ReadEncodedValue();
            var algorithm = csr.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcdsaSha256Oid || algorithm.HasData)
                return false;

            signature = csr.ReadBitString(out var unusedBits);
            if (unusedBits != 0 || csr.HasData || reader.HasData)
                return false;

            var infoReader = new AsnReader(requestInfo, AsnEncodingRules.DER).ReadSequence();
            if (infoReader.ReadInteger() != BigInteger.Zero)
                return false;
            infoReader.ReadEncodedValue();
            publicKey = infoReader.ReadEncodedValue().ToArray();
            return publicKey.Length > 0;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static bool TryReadCertificate(
        ReadOnlyMemory<byte> der,
        out ReadOnlyMemory<byte> tbs,
        out byte[] publicKey,
        out byte[] signature)
    {
        tbs = default;
        publicKey = Array.Empty<byte>();
        signature = Array.Empty<byte>();
        try
        {
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            var certificate = reader.ReadSequence();
            tbs = certificate.ReadEncodedValue();
            var algorithm = certificate.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcdsaSha256Oid || algorithm.HasData)
                return false;

            signature = certificate.ReadBitString(out var unusedBits);
            if (unusedBits != 0 || certificate.HasData || reader.HasData)
                return false;

            var infoReader = new AsnReader(tbs, AsnEncodingRules.DER).ReadSequence();
            var first = infoReader.ReadEncodedValue();
            if (first.Length == 0)
                return false;
            if (first.Span[0] != 0xa0)
                return false;
            infoReader.ReadEncodedValue();
            infoReader.ReadEncodedValue();
            infoReader.ReadEncodedValue();
            infoReader.ReadEncodedValue();
            infoReader.ReadEncodedValue();
            publicKey = infoReader.ReadEncodedValue().ToArray();
            return publicKey.Length > 0;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }
}
