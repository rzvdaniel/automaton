using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Automaton.Node.Enrollment;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeEnrollmentKeyMaterialTests
{
    [Test]
    public void Enrollment_material_contains_a_matching_signed_csr_and_self_signed_leaf()
    {
        var now = DateTimeOffset.UtcNow;
        using var material = NodeEnrollmentKeyMaterial.Create("CN=synthetic-node", now);
        using var certificate = X509CertificateLoader.LoadCertificate(material.CertificateDer);
        using var key = certificate.GetECDsaPublicKey();

        Assert.Multiple(() =>
        {
            Assert.That(certificate.Subject, Is.EqualTo(certificate.Issuer));
            Assert.That(certificate.NotAfter - certificate.NotBefore, Is.LessThanOrEqualTo(TimeSpan.FromDays(90)));
            Assert.That(Convert.ToHexString(SHA256.HashData(key!.ExportSubjectPublicKeyInfo())), Is.EqualTo(material.PinnedPublicKeySha256));
            Assert.That(material.CsrDer, Is.Not.Empty);
        });
    }

    [Test]
    public void Recovery_signature_is_created_by_the_pinned_installation_key()
    {
        using var material = NodeEnrollmentKeyMaterial.Create("CN=synthetic-node", DateTimeOffset.UtcNow);
        var message = System.Text.Encoding.UTF8.GetBytes("synthetic recovery challenge");
        using var certificate = X509CertificateLoader.LoadCertificate(material.CertificateDer);
        using var publicKey = certificate.GetECDsaPublicKey();
        var signature = material.SignRecoveryChallenge(message);

        Assert.That(publicKey!.VerifyData(message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence), Is.True);
    }
}
