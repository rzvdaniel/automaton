using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Exits the Runner when its mounted Docker socket stops answering, so the Docker restart policy starts it again with a
/// fresh mount. After a Docker restart with live-restore the container keeps a mount of the old, dead socket (#1769).
/// </summary>
public sealed class DockerSocketWatchdogService(IHostApplicationLifetime lifetime) : BackgroundService
{
    public const string SocketPath = "/var/run/docker.sock";
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    public const int FailuresBeforeExit = 3;

    /// <summary>Counts consecutive failed probes and says when the Runner should exit.</summary>
    public sealed class FailureCounter
    {
        private int failures;
        public bool Record(bool answered)
        {
            failures = answered ? 0 : failures + 1;
            return failures >= FailuresBeforeExit;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only a Runner that was given the socket (a container with the mount) watches it.
        if (!File.Exists(SocketPath)) return;
        var counter = new FailureCounter();
        using var timer = new PeriodicTimer(Interval);
        while (await WaitAsync(timer, stoppingToken))
        {
            if (!counter.Record(await PingAsync(stoppingToken))) continue;
            Log.Error("The Docker socket has not answered for {Seconds} seconds. Exiting so Docker restarts the Runner with a fresh mount.",
                (int)Interval.TotalSeconds * FailuresBeforeExit);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
            return;
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }

    private static async Task<bool> PingAsync(CancellationToken token)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancel) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cancel); return new NetworkStream(socket, true); }
                    catch { socket.Dispose(); throw; }
                }
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("http://localhost/_ping", token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!token.IsCancellationRequested) { return false; }
    }
}
