using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Automaton.Node.Activation;
using Automaton.Node.Preparation;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeActivationRedirectTests
{
    [Test]
    public async Task Activation_transport_does_not_forward_api_key_to_redirect_target()
    {
        using var scenario = await RedirectScenario.CreateAsync();
        var result = await scenario.SendRequestAsync();
        Assert.Multiple(() =>
        {
            Assert.That(scenario.AllowAutoRedirect, Is.False);
            Assert.That(result.OriginHeaders, Does.Contain("X-API-Key: synthetic-api-key"));
            Assert.That(result.TargetReceivedConnection, Is.False);
        });
    }

    private sealed class RedirectScenario : IDisposable
    {
        private readonly LoopbackTlsListener target;
        private readonly LoopbackTlsListener origin;
        private readonly CancellationTokenSource targetTimeout;
        private readonly Task<bool> targetConnection;
        private readonly NodeActivationClient client;
        private readonly SocketsHttpHandler handler;

        private RedirectScenario(LoopbackTlsListener target, LoopbackTlsListener origin,
            CancellationTokenSource targetTimeout, Task<bool> targetConnection, NodeActivationClient client,
            SocketsHttpHandler handler)
        {
            this.target = target;
            this.origin = origin;
            this.targetTimeout = targetTimeout;
            this.targetConnection = targetConnection;
            this.client = client;
            this.handler = handler;
        }

        public bool AllowAutoRedirect => handler.AllowAutoRedirect;

        private static NodeIdentityManifest Identity() => new(1,
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"), "1", new string('f', 64),
            Guid.Parse("80000000-0000-0000-0000-000000000001"), new string('a', 64), "node-client-key.pem", "node-api-key");

        private static NodeExternalAuthorityEvidence Authority() => new(
            Guid.Parse("40000000-0000-0000-0000-000000000001"), 3, "lab-revision-1", new string('b', 64));

        public static async Task<RedirectScenario> CreateAsync()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx, "synthetic-test-only"),
                "synthetic-test-only", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
            var target = new LoopbackTlsListener(certificate, ownsCertificate: false);
            var origin = new LoopbackTlsListener(certificate, ownsCertificate: true);
            var targetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var targetConnection = target.AcceptAnyConnectionAsync(targetTimeout.Token);
            var handler = NodeActivationClient.CreateTransportHandler();
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            var client = new NodeActivationClient(handler, new Uri(origin.Origin), null!);
            return new RedirectScenario(target, origin, targetTimeout, targetConnection, client, handler);
        }

        public async Task<RedirectResult> SendRequestAsync()
        {
            var sourceRequest = origin.AcceptAndRedirectAsync(new Uri(target.Origin + "/capture-api-key"));
            Exception? clientException = null;
            try
            {
                _ = await client.ReadCurrentAsync(Identity(), Authority(), [], "synthetic-api-key", CancellationToken.None);
            }
            catch (Exception exception)
            {
                clientException = exception;
            }
            var headers = await sourceRequest;
            Assert.That(clientException, Is.TypeOf<InvalidDataException>());
            return new RedirectResult(headers, await targetConnection);
        }

        public void Dispose()
        {
            client.Dispose();
            handler.Dispose();
            targetTimeout.Dispose();
            target.DisposeAsync().AsTask().GetAwaiter().GetResult();
            origin.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed record RedirectResult(string OriginHeaders, bool TargetReceivedConnection);

    private sealed class LoopbackTlsListener : IAsyncDisposable
    {
        private readonly X509Certificate2 certificate;
        private readonly bool ownsCertificate;
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        public LoopbackTlsListener(X509Certificate2 certificate, bool ownsCertificate)
        {
            this.certificate = certificate;
            this.ownsCertificate = ownsCertificate;
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Origin = $"https://127.0.0.1:{endpoint.Port}";
        }

        public string Origin { get; }

        public async Task<string> AcceptAndRedirectAsync(Uri redirectTarget)
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var tls = await AuthenticateAsync(client.GetStream(), CancellationToken.None);
            using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
            _ = await reader.ReadLineAsync();
            var headers = new StringBuilder();
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    break;
                headers.AppendLine(line);
            }
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {redirectTarget}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await tls.WriteAsync(response);
            await tls.FlushAsync();
            return headers.ToString();
        }

        public async Task<bool> AcceptAnyConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public ValueTask DisposeAsync()
        {
            listener.Stop();
            if (ownsCertificate)
                certificate.Dispose();
            return ValueTask.CompletedTask;
        }

        private Task<SslStream> AuthenticateAsync(Stream networkStream, CancellationToken cancellationToken)
        {
            var tls = new SslStream(networkStream, leaveInnerStreamOpen: true);
            return AuthenticateCoreAsync(tls, cancellationToken);
        }

        private async Task<SslStream> AuthenticateCoreAsync(SslStream tls, CancellationToken cancellationToken)
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, cancellationToken);
            return tls;
        }
    }
}
