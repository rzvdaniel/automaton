using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Automaton.Node.Enrollment;

/// <summary>Ephemeral enrollment proof material; persistence and activation are intentionally separate.</summary>
public sealed class NodeEnrollmentKeyMaterial : IDisposable
{
    private readonly ECDsa privateKey;

    private NodeEnrollmentKeyMaterial(ECDsa privateKey, byte[] csrDer, byte[] certificateDer, byte[] publicKeyDer)
    {
        this.privateKey = privateKey;
        CsrDer = csrDer;
        CertificateDer = certificateDer;
        PinnedPublicKeySha256 = Convert.ToHexString(SHA256.HashData(publicKeyDer));
    }

    public byte[] CsrDer { get; }
    public byte[] CertificateDer { get; }
    public string PinnedPublicKeySha256 { get; }

    public static NodeEnrollmentKeyMaterial Create(string certificateSubject, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateSubject);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(certificateSubject, key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(now.AddMinutes(-1), now.AddDays(89));
        var retainedKey = ECDsa.Create();
        retainedKey.ImportPkcs8PrivateKey(key.ExportPkcs8PrivateKey(), out _);
        return new NodeEnrollmentKeyMaterial(
            retainedKey,
            request.CreateSigningRequest(),
            certificate.RawData,
            key.ExportSubjectPublicKeyInfo());
    }

    public byte[] SignRecoveryChallenge(ReadOnlySpan<byte> message) =>
        privateKey.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    public void Dispose() => privateKey.Dispose();
}
