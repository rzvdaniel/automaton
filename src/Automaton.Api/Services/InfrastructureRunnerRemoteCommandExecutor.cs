using System.Text.RegularExpressions;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Executes host-management scripts through the same Runner process used for
/// normal workloads. SSH remains the bootstrap/recovery path; once an
/// infrastructure Runner is online, the API uses its authenticated SignalR
/// connection instead.
/// </summary>
public sealed class InfrastructureRunnerRemoteCommandExecutor(
    ApplicationDbContext db,
    IHubContext<AutomatonHub> automatonHub,
    ILogger<InfrastructureRunnerRemoteCommandExecutor> logger) : IInfrastructureRunnerCommandExecutor
{
    public async Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken)
    {
        if (!host.InfrastructureRunnerId.HasValue)
        {
            return Failure("Runner host has no infrastructure Runner linked.");
        }

        var runner = await db.Runners.FirstOrDefaultAsync(
            x => x.Id == host.InfrastructureRunnerId.Value,
            cancellationToken);

        if (runner == null)
        {
            return Failure("The linked infrastructure Runner was not found.");
        }

        if (!RunnerCapabilities.Includes(
                runner.CapabilitiesJson,
                RunnerCapabilities.HostedRunnerProvisioning,
                runner.Purpose))
        {
            return Failure("The linked Runner does not advertise HostedRunnerProvisioning capability.");
        }

        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            return Failure($"The infrastructure Runner '{runner.Name}' is not online.");
        }

        try
        {
            var response = await automatonHub.Clients.Client(runner.ConnectionId!)
                .InvokeAsync<InfrastructureCommandResponse>(
                    AutomatonHubMethods.RunInfrastructureCommand,
                    new InfrastructureCommandRequest
                    {
                        Script = script,
                        TimeoutSeconds = 300
                    },
                    cancellationToken);

            return new HostedRunnerRemoteExecutionResult(
                response.Success,
                response.ExitStatus,
                RedactSecrets(response.Output),
                RedactSecrets(response.Error));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Infrastructure Runner command failed for runner host {RunnerHostId}", host.Id);
            return Failure(ex.Message);
        }
    }

    private static HostedRunnerRemoteExecutionResult Failure(string message)
        => new(false, -1, string.Empty, message);

    private static string RedactSecrets(string value)
        => Regex.Replace(value ?? string.Empty, @"ak_[A-Za-z0-9_-]+", "ak_***", RegexOptions.CultureInvariant);
}
