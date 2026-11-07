using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.ServerHealth;

/// <summary>Reads the expiry date of the certificate a host serves on port 443. Results are cached for 30 minutes.</summary>
public sealed class CertificateExpiryProbe(IMemoryCache cache)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<CertificateInfo> ProbeAsync(string host, DateTime nowUtc, CancellationToken token)
    {
        var key = "certificate:" + host;
        if (cache.TryGetValue(key, out CertificateInfo? cached) && cached is not null) return cached;
        var info = await ReadAsync(host, nowUtc, token);
        cache.Set(key, info, TimeSpan.FromMinutes(30));
        return info;
    }

    public static int DaysRemaining(DateTime notAfterUtc, DateTime nowUtc) => (int)Math.Floor((notAfterUtc - nowUtc).TotalDays);

    private static async Task<CertificateInfo> ReadAsync(string host, DateTime nowUtc, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, 443, timeout.Token);
            DateTime? notAfter = null;
            // Only the date is read, so an expired or untrusted certificate is still reported instead of failing.
            await using var tls = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
            {
                notAfter = certificate is null ? null : new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate).NotAfter.ToUniversalTime();
                return true;
            });
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);
            return notAfter is { } expires
                ? new(host, expires, DaysRemaining(expires, nowUtc), null)
                : new(host, null, null, "The certificate has no readable expiry date.");
        }
        catch (Exception exception) when (exception is SocketException or IOException or System.Security.Authentication.AuthenticationException
            or OperationCanceledException && !token.IsCancellationRequested)
        {
            return new(host, null, null, "The certificate could not be read.");
        }
    }
}
