using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Data;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Config;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Handlers;

public sealed class ManagedRunnerProvisionRequestedEventHandler(
    ApplicationDbContext dbContext,
    IHostedRunnerRemoteCommandExecutor remoteCommandExecutor,
    IEventPublisher eventPublisher,
    IOptionsSnapshot<HostedRunnerOptions> options,
    ILogger<ManagedRunnerProvisionRequestedEventHandler> logger) : IEventHandler<ManagedRunnerProvisionRequestedEvent>
{
    public async Task HandleAsync(ManagedRunnerProvisionRequestedEvent message, CancellationToken cancellationToken = default)
    {
        var hostedRunner = await dbContext.HostedRunners
            .Include(x => x.RunnerHost)
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x => x.Id == message.HostedRunnerId && x.DeletedAt == null, cancellationToken);

        if (hostedRunner == null)
        {
            logger.LogWarning("Managed runner provisioning skipped because hosted runner {HostedRunnerId} no longer exists", message.HostedRunnerId);
            return;
        }

        var runnerHost = hostedRunner.RunnerHost;
        if (runnerHost == null)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = "No runner host is assigned for managed runtime provisioning.";
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventPublisher.PublishAsync(new ManagedRunnerProvisionFailedEvent
            {
                HostedRunnerId = hostedRunner.Id,
                UserId = hostedRunner.UserId,
                Error = hostedRunner.LastError,
                CorrelationId = message.CorrelationId
            }, cancellationToken);
            return;
        }

        if (!options.Value.Enabled)
        {
            logger.LogInformation("Managed runner provisioning skipped for hosted runner {HostedRunnerId} because HostedRunners:Enabled=false", hostedRunner.Id);
            return;
        }

        if (!string.Equals(options.Value.DeploymentMode, HostedRunnerDeploymentModes.SshDocker, StringComparison.Ordinal))
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = $"Unsupported hosted runner deployment mode '{options.Value.DeploymentMode}'.";
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventPublisher.PublishAsync(new ManagedRunnerProvisionFailedEvent
            {
                HostedRunnerId = hostedRunner.Id,
                UserId = hostedRunner.UserId,
                Error = hostedRunner.LastError,
                CorrelationId = message.CorrelationId
            }, cancellationToken);
            return;
        }

        var deployment = hostedRunner.Deployments
            .Where(x => x.Action == HostedRunnerDeploymentActions.Provision)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefault();

        var script = HostedRunnerDockerCommandBuilder.BuildProvisionScript(hostedRunner, options.Value, message.RunnerApiKey);
        var executionResult = await remoteCommandExecutor.ExecuteProvisionScriptAsync(runnerHost, script, cancellationToken);

        if (!executionResult.Succeeded)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = string.IsNullOrWhiteSpace(executionResult.StandardError)
                ? $"Remote provisioning command failed with exit status {executionResult.ExitStatus}."
                : executionResult.StandardError;
            hostedRunner.UpdatedAt = DateTime.UtcNow;

            if (deployment != null)
            {
                deployment.Status = HostedRunnerDeploymentStatuses.Failed;
                deployment.Logs = executionResult.StandardOutput;
                deployment.Error = hostedRunner.LastError;
                deployment.FinishedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await eventPublisher.PublishAsync(new ManagedRunnerProvisionFailedEvent
            {
                HostedRunnerId = hostedRunner.Id,
                UserId = hostedRunner.UserId,
                Error = hostedRunner.LastError,
                CorrelationId = message.CorrelationId
            }, cancellationToken);
            return;
        }

        hostedRunner.ContainerId = executionResult.ContainerId;
        hostedRunner.Status = HostedRunnerStatuses.Provisioning;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;

        if (deployment != null)
        {
            deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
            deployment.Logs = executionResult.StandardOutput;
            deployment.Error = string.IsNullOrWhiteSpace(executionResult.StandardError) ? null : executionResult.StandardError;
            deployment.FinishedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAsync(new ManagedRunnerProvisionCompletedEvent
        {
            HostedRunnerId = hostedRunner.Id,
            UserId = hostedRunner.UserId,
            CorrelationId = message.CorrelationId
        }, cancellationToken);
    }
}
