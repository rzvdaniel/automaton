using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Middleware;
using Automaton.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class NodeAuthenticationBoundaryTests
{
    [Test]
    public void Traefik_header_decodes_one_certificate_and_hashes_the_der()
    {
        using var certificate = CreateCertificate();
        var encoded = Convert.ToBase64String(certificate.RawData);
        var headers = new HeaderDictionary { ["X-Forwarded-Tls-Client-Cert"] = encoded };

        Assert.That(NodeCertificateHeaderDecoder.TryDecode(headers, out var decoded), Is.True);
        Assert.That(decoded!.Certificate.RawData, Is.EqualTo(certificate.RawData));
        Assert.That(decoded.Sha256DerFingerprint, Is.EqualTo(SHA256.HashData(certificate.RawData)));
    }

    [Test]
    public void Traefik_v3_3_6_golden_header_fixture_decodes_to_the_recorded_der_fingerprint()
    {
        var fixturePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "Fixtures",
            "traefik-v3.3.6-pass-tls-client-cert.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = fixture.RootElement;
        var header = root.GetProperty("headerValue").GetString();
        var expectedFingerprint = Convert.FromHexString(root.GetProperty("sha256DerFingerprint").GetString()!);
        var image = root.GetProperty("image").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(image, Is.EqualTo("traefik@sha256:83f3c843133530e4aa45e6ddc415488583a8a01c88adc5022d415c6e97ceeaae"));
            Assert.That(header, Does.Not.Contain("-----BEGIN"));
            Assert.That(header, Does.Not.Contain("\n"));
            Assert.That(header, Does.Not.Contain(","));
        });

        var headers = new HeaderDictionary { ["X-Forwarded-Tls-Client-Cert"] = header };
        Assert.That(NodeCertificateHeaderDecoder.TryDecode(headers, out var decoded), Is.True);
        Assert.That(decoded!.Sha256DerFingerprint, Is.EqualTo(expectedFingerprint));
    }

    [TestCase(" ")]
    [TestCase("\"abc\"")]
    [TestCase("YWJj,REVG")]
    [TestCase("YWJj\n")]
    [TestCase("YWJj====")]
    public void Traefik_header_rejects_noncanonical_and_multiple_values(string value)
    {
        Assert.That(NodeCertificateHeaderDecoder.TryDecode(value, out _), Is.False);
    }

    [Test]
    public void Traefik_header_rejects_a_certificate_followed_by_trailing_der()
    {
        using var certificate = CreateCertificate();
        var derWithTrailingByte = certificate.RawData.Concat(new byte[] { 0 }).ToArray();

        Assert.That(NodeCertificateHeaderDecoder.TryDecode(Convert.ToBase64String(derWithTrailingByte), out _), Is.False);
    }

    [Test]
    public void Traefik_header_rejects_duplicate_header_fields()
    {
        using var certificate = CreateCertificate();
        var encoded = Convert.ToBase64String(certificate.RawData);
        var headers = new HeaderDictionary
        {
            ["X-Forwarded-Tls-Client-Cert"] = new StringValues(new[] { encoded, encoded })
        };

        Assert.That(NodeCertificateHeaderDecoder.TryDecode(headers, out _), Is.False);
    }

    [Test]
    public void Enrollment_rejects_a_valid_p384_csr_and_certificate()
    {
        var now = DateTime.UtcNow;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=synthetic-p384-node", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(now.AddMinutes(-1), now.AddDays(1));
        var csr = request.CreateSigningRequest();
        var pin = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

        Assert.That(
            NodeEnrollmentCryptography.ValidateEnrollmentProof(csr, certificate.RawData, pin, now, out _),
            Is.False);
    }

    [TestCase("GET", "/api/node/v1/identity", 7101)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/complete", 7101)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/recovery/challenge", 7101)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/recovery/complete", 7102)]
    [TestCase("GET", "/api/node/v1/identity", 5000)]
    [TestCase("GET", "/api/node/v1/identity", 7103)]
    public async Task Cross_listener_request_is_rejected_even_with_spoofed_identity_headers(
        string method,
        string path,
        int localPort)
    {
        var options = Options.Create(new NodeListenerOptions { ManagementPort = 7102, BootstrapPort = 7103 });
        var invoked = false;
        var middleware = new NodeListenerRouteIsolationMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, options);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.LocalPort = localPort;
        context.Request.Headers["X-Forwarded-Tls-Client-Cert"] = "forged";
        context.Request.Headers["X-API-Key"] = "synthetic-invalid-key";

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(context.Response.Headers.CacheControl, Is.EqualTo("no-store"));
            Assert.That(invoked, Is.False);
        });
    }

    [Test]
    public async Task Ordinary_api_listener_rejects_node_paths_when_node_ports_are_unconfigured()
    {
        var options = Options.Create(new NodeListenerOptions());
        var invoked = false;
        var middleware = new NodeListenerRouteIsolationMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, options);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/complete";
        context.Connection.LocalPort = 5000;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(invoked, Is.False);
    }

    [TestCase("GET", "/api/users", 7102)]
    [TestCase("GET", "/api/workflow/hub", 7102)]
    [TestCase("GET", "/healthz", 7102)]
    [TestCase("GET", "/not-a-mapped-route", 7102)]
    [TestCase("GET", "/api/users", 7103)]
    [TestCase("GET", "/api/workflow/hub", 7103)]
    [TestCase("GET", "/healthz", 7103)]
    [TestCase("GET", "/not-a-mapped-route", 7103)]
    [TestCase("GET", "/api/node/v1/identity", 7103)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/complete", 7102)]
    public async Task Isolated_listener_rejects_every_route_outside_its_complete_allow_list(
        string method,
        string path,
        int localPort)
    {
        var options = Options.Create(new NodeListenerOptions { ManagementPort = 7102, BootstrapPort = 7103 });
        var invoked = false;
        var middleware = new NodeListenerRouteIsolationMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, options);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.LocalPort = localPort;

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(invoked, Is.False);
        });
    }

    [TestCase("GET", "/api/node/v1/identity", true)]
    [TestCase("POST", "/api/node/v1/activation", true)]
    [TestCase("POST", "/api/node/v1/identity", false)]
    [TestCase("GET", "/api/node/v1/assignments", false)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/complete", false)]
    public void Management_listener_allows_only_the_implemented_read_only_identity_route(string method, string path, bool expected)
    {
        Assert.That(
            NodeListenerRoutePolicy.Allows(method, path, 7102, new NodeListenerOptions { ManagementPort = 7102, BootstrapPort = 7103 }),
            Is.EqualTo(expected));
    }

    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/complete", true)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/bootstrap", false)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/recovery/challenge", true)]
    [TestCase("GET", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/recovery/challenge", false)]
    [TestCase("POST", "/api/node-enrollments/not-a-guid/complete", false)]
    [TestCase("POST", "/api/node-enrollments/18d38dcc-6a44-4c6f-a35a-9b784436fac1/complete/extra", false)]
    public void Bootstrap_listener_allows_only_exact_post_contracts(string method, string path, bool expected)
    {
        Assert.That(
            NodeListenerRoutePolicy.Allows(method, path, 7103, new NodeListenerOptions { BootstrapPort = 7103 }),
            Is.EqualTo(expected));
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=synthetic-node", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
