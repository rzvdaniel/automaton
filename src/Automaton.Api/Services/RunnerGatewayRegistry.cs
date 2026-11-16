using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Automaton.Api.Services;

/// <summary>A Runner gateway the proxy can reach directly (#1181), with the secret the proxy adds to each request.</summary>
public sealed record RunnerGateway(Uri Url, string Secret);

/// <summary>
/// The Runners whose runtime gateway answered the server's probe. Only these get direct proxy routes; every other
/// Runner (remote, older, unreachable) stays on the SignalR tunnel. Kept in memory: a Runner reports again whenever it
/// connects, and until then its apps are served through the tunnel.
/// </summary>
public sealed class RunnerGatewayRegistry
{
    public const string SecretHeader = "X-Automaton-Gateway-Secret";

    private readonly ConcurrentDictionary<Guid, RunnerGateway> gateways = new();
    private readonly Func<RunnerGateway, CancellationToken, Task<bool>> probe;

    public RunnerGatewayRegistry() : this(ProbeAsync) { }

    internal RunnerGatewayRegistry(Func<RunnerGateway, CancellationToken, Task<bool>> probe) => this.probe = probe;

    public RunnerGateway? Find(Guid runnerId) => gateways.TryGetValue(runnerId, out var gateway) ? gateway : null;

    /// <summary>Probes the reported gateway and remembers it. Returns true when the set of direct routes changed.</summary>
    public async Task<bool> ReportAsync(Guid runnerId, string? url, string? secret, CancellationToken cancellationToken)
    {
        if (!TryParse(url, secret, out var gateway) || !await probe(gateway!, cancellationToken))
            return gateways.TryRemove(runnerId, out _);

        var previous = gateways.TryGetValue(runnerId, out var existing) ? existing : null;
        gateways[runnerId] = gateway!;
        return previous is null || previous.Url != gateway!.Url || previous.Secret != gateway.Secret;
    }

    /// <summary>Only a plain http address with a private IP literal and a port: the server never calls out to names or public hosts.</summary>
    internal static bool TryParse(string? url, string? secret, out RunnerGateway? gateway)
    {
        gateway = null;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length is < 16 or > 200 || secret.Any(c => c <= ' ' || c > '~')) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp) return false;
        if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.UserInfo.Length > 0 || uri.IsDefaultPort) return false;
        if (uri.Port < 1024 || !IPAddress.TryParse(uri.Host, out var address) || !IsPrivateIPv4(address)) return false;
        gateway = new RunnerGateway(new Uri($"http://{address}:{uri.Port}"), secret);
        return true;
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private static readonly HttpClient ProbeClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static async Task<bool> ProbeAsync(RunnerGateway gateway, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(gateway.Url, "/gateway-check"));
            request.Headers.Add(SecretHeader, gateway.Secret);
            using var response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
