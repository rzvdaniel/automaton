using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// With each heartbeat, tells the server which app runtimes this Runner could not start (and which came back), so
/// Studio shows them down with Retry instead of live. An older server that does not know the method is ignored.
/// </summary>
internal sealed class RunnerRuntimeStateSync(RunnerRuntimeRegistry? registry, ILogger logger)
{
    public async Task SyncAsync(HubConnection hub, CancellationToken cancellationToken)
    {
        if (registry is null) return;
        var pending = registry.StateReports();
        if (pending.Count == 0) return;
        try
        {
            await hub.InvokeAsync("ReportRuntimeStates",
                pending.Select(item => new RuntimeStateReport(item.AppId, item.DeploymentTargetId, item.Failed, item.Error)).ToArray(),
                cancellationToken);
            registry.Acknowledge(pending);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Debug(exception, "Could not report runtime states");
        }
    }
}
