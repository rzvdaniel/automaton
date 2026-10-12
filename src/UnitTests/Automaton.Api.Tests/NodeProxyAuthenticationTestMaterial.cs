using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Automaton.Api.Config;
using Automaton.Api.Security;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal sealed class NodeProxyAuthenticationTestMaterial : IDisposable
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    private readonly ECDsa rootKey;

    private NodeProxyAuthenticationTestMaterial(ECDsa rootKey, X509Certificate2 rootCertificate)
    {
        this.rootKey = rootKey;
        RootCertificate = rootCertificate;
    }

    public X509Certificate2 RootCertificate { get; }

    public static NodeProxyAuthenticationTestMaterial Create(DateTimeOffset now)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=synthetic-test-edge-root", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var root = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));
        return new NodeProxyAuthenticationTestMaterial(key, root);
    }

    public X509Certificate2 CreateTraefikLeaf(
        DateTimeOffset now,
        string uriSan = NodeProxyAuthenticationOptions.RequiredUriSan,
        bool includeClientAuthenticationEku = true)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=synthetic-test-traefik", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        if (includeClientAuthenticationEku)
        {
            var usages = new OidCollection { new Oid(ClientAuthenticationOid) };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        }
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddUri(new Uri(uriSan, UriKind.Absolute));
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        return request.Create(RootCertificate, now.AddMinutes(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
    }

    public NodeProxyCertificateValidator CreateValidator(params X509Certificate2[] allowedLeaves)
    {
        var options = new NodeProxyAuthenticationOptions
        {
            TrustedRootCertificateBase64 = Convert.ToBase64String(RootCertificate.RawData),
            AllowedLeafSha256Fingerprints = allowedLeaves
                .Select(certificate => Convert.ToHexString(SHA256.HashData(certificate.RawData)))
                .ToArray()
        };
        return new NodeProxyCertificateValidator(new StaticOptionsMonitor<NodeProxyAuthenticationOptions>(options));
    }

    public void Dispose()
    {
        RootCertificate.Dispose();
        rootKey.Dispose();
    }
}

internal sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    where TOptions : class
{
    public TOptions CurrentValue { get; } = value;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}
