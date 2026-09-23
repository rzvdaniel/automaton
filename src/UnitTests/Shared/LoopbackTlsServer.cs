using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Automaton.UnitTests.Shared;

internal sealed class LoopbackTlsServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly RSA key = RSA.Create(2048);
    private readonly X509Certificate2 certificate;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task serverTask;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public bool AcceptedConnection { get; private set; }
    public bool ReceivedApplicationData { get; private set; }
    public Exception? ServerException { get; private set; }
    public Task Completion => serverTask;

    public LoopbackTlsServer()
    {
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        certificate = X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        listener.Start();
        serverTask = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(stopping.Token);
            AcceptedConnection = true;
            using var stream = new SslStream(client.GetStream());
            await stream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions { ServerCertificate = certificate }, stopping.Token);

            // A client accepting this certificate would reach the protocol response.
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, stopping.Token);
            if (read > 0)
            {
                ReceivedApplicationData = true;
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
                await stream.WriteAsync(response, stopping.Token);
            }
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException or SocketException or OperationCanceledException)
        {
            ServerException = exception;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        await serverTask;
        certificate.Dispose();
        key.Dispose();
        stopping.Dispose();
    }
}
