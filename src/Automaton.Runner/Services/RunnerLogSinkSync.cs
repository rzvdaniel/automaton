using Automaton.Core.Logs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// With each heartbeat, tells the server which app runtimes this Runner hosts and sends the Runner's own logs to the app
/// that receives them (<c>logs:receive</c>) when the server names one here. Does nothing when the admin set an endpoint.
/// </summary>
internal sealed class RunnerLogSinkSync(RunnerRuntimeRegistry? registry, ILogger logger)
{
    // Heartbeats build a new instance each time, so the last failure is kept per process.
    private static string? lastFailure;

    public async Task SyncAsync(HubConnection hub, CancellationToken cancellationToken)
    {
        if (registry is null || !LogForwarding.IsSwitchable) return;
        try
        {
            var reports = Reports(registry.Snapshot());
            var assignment = await hub.InvokeAsync<LogSinkAssignment?>("SyncLogSink", reports, cancellationToken);
            LogForwarding.SetAddress(assignment?.Address);
            lastFailure = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // An older server does not know the method, or the server is unreachable: stop sending until it answers again.
            LogForwarding.SetAddress(null);
            // Warn once per distinct failure, not every heartbeat; a later success re-arms it.
            if (lastFailure != exception.Message)
            {
                lastFailure = exception.Message;
                logger.Warning(exception, "Could not sync the log receiver; logs are not shipped until it works");
            }
        }
    }

    internal static LogSinkRuntimeReport[] Reports(IReadOnlyList<RunnerRuntimeRegistration> runtimes)
    {
        string host;
        try { host = RunnerRuntimeEndpointConfiguration.HostAddress; }
        catch (InvalidOperationException) { return []; }
        return runtimes.Select(runtime => new LogSinkRuntimeReport(runtime.AppId, runtime.DeploymentTargetId, $"http://{host}:{runtime.Port}")).ToArray();
    }
}
