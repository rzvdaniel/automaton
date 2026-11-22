using System.Net.WebSockets;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// The Runner's end of the app tunnel. It opens a few outbound WebSocket connections to the API (no inbound port, no
/// extra setup) and serves HTTP/2 on each with Kestrel, so the API's requests arrive as real HTTP/2 streams with
/// flow control, cancellation and multiplexing from the framework instead of our own protocol.
/// </summary>
public sealed class RunnerTunnel(
    ConfigurationService configService,
    IOptions<TunnelForwarderConfig> options,
    MachineStaticSitePublisher? staticSitePublisher = null) : IAsyncDisposable
{
    internal const string ConnectPath = "api/runner-tunnel/connect";
    private static readonly Serilog.ILogger Logger = Log.ForContext<RunnerTunnel>();

    private readonly TunnelForwarderConfig config = options.Value;
    private readonly RunnerTunnelListener listener = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private WebApplication? app;
    private RunnerTunnelRequestHandler? handler;
    private CancellationTokenSource? dialing;
    private Task[] dialLoops = [];

    /// <summary>Called before each request; false while the Runner is switching to another API instance.</summary>
    internal Func<bool> TryBeginRequest { get; set; } = () => true;

    /// <summary>Called when an accepted request has ended.</summary>
    internal Action CompleteRequest { get; set; } = () => { };

    /// <summary>Opens the tunnel connections, replacing any open ones (a new hub connection means a new API instance may be active).</summary>
    public async Task StartAsync(string apiKey, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await StopDialingAsync();
            await EnsureServerAsync(cancellationToken);
            var source = new CancellationTokenSource();
            dialing = source;
            var address = BuildAddress(configService.BaseUrl);
            dialLoops = Enumerable.Range(0, Math.Max(1, config.TunnelConnections))
                .Select(index => Task.Run(() => DialLoopAsync(index, address, apiKey, source.Token), CancellationToken.None))
                .ToArray();
        }
        finally { gate.Release(); }
    }

    /// <summary>Closes the tunnel connections; requests in flight on them end.</summary>
    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { await StopDialingAsync(); }
        finally { gate.Release(); }
    }

    private async Task StopDialingAsync()
    {
        if (dialing is null) return;
        dialing.Cancel();
        try { await Task.WhenAll(dialLoops).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        dialing.Dispose();
        dialing = null;
        dialLoops = [];
    }

    /// <summary>Serves HTTP/2 on an open tunnel connection. The dial loops call this with each socket they open.</summary>
    public async Task AcceptConnectionAsync(TunnelConnectionStream stream, CancellationToken cancellationToken)
    {
        await EnsureServerAsync(cancellationToken);
        listener.Enqueue(stream);
    }

    private async Task EnsureServerAsync(CancellationToken cancellationToken)
    {
        if (app is not null) return;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpForwarder();
        builder.Services.AddSingleton<IConnectionListenerFactory>(listener);
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Limits.Http2.InitialStreamWindowSize = config.Http2StreamWindowBytes;
            kestrel.Limits.Http2.InitialConnectionWindowSize = config.Http2ConnectionWindowBytes;
            kestrel.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
            kestrel.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(20);
            kestrel.Limits.MaxRequestBodySize = null;
            kestrel.Listen(listener.EndPoint, endpoint => endpoint.Protocols = HttpProtocols.Http2);
        });
        var built = builder.Build();
        handler = new RunnerTunnelRequestHandler(
            built.Services.GetRequiredService<Yarp.ReverseProxy.Forwarder.IHttpForwarder>(), config, staticSitePublisher)
        {
            TryBeginRequest = () => TryBeginRequest(),
            CompleteRequest = () => CompleteRequest()
        };
        built.UseWebSockets();
        built.Run(handler.HandleAsync);
        await built.StartAsync(cancellationToken);
        app = built;
    }

    // http(s)://host/ -> ws(s)://host/api/runner-tunnel/connect
    internal static Uri BuildAddress(string baseUrl)
    {
        var uri = new UriBuilder(new Uri(baseUrl.TrimEnd('/') + "/" + ConnectPath));
        uri.Scheme = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        if (uri.Uri.IsDefaultPort) uri.Port = -1;
        return uri.Uri;
    }

    private async Task DialLoopAsync(int index, Uri address, string apiKey, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("X-API-Key", apiKey);
                socket.Options.SetRequestHeader("RunnerId", configService.RunnerId);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await socket.ConnectAsync(address, cancellationToken);
                failures = 0;
                var stream = new TunnelConnectionStream(WebSocketStream.Create(socket, WebSocketMessageType.Binary, ownsWebSocket: false));
                Logger.Debug("Tunnel connection {Index} open", index);
                await AcceptConnectionAsync(stream, cancellationToken);
                await Task.WhenAny(stream.Closed, Task.Delay(Timeout.Infinite, cancellationToken));
                await stream.DisposeAsync();
                Logger.Debug("Tunnel connection {Index} ended", index);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                failures++;
                Logger.Debug(ex, "Tunnel connection {Index} failed (attempt {Failures})", index, failures);
            }

            // A short pause after a clean end, longer and jittered after repeated failures.
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(failures, 5))) + Random.Shared.NextDouble());
            try { await Task.Delay(failures == 0 ? TimeSpan.FromSeconds(1) : delay, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (app is not null) await app.DisposeAsync();
    }
}
