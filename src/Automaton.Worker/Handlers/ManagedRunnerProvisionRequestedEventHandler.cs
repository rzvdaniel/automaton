using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Handlers;

public sealed class ManagedRunnerProvisionRequestedEventHandler(
    ApplicationDbContext dbContext,
    IEventPublisher eventPublisher,
    IOptionsSnapshot<HostedRunnerOptions> options,
    ILogger<ManagedRunnerProvisionRequestedEventHandler> logger) : IEventHandler<ManagedRunnerProvisionRequestedEvent>
{
    private const string ProvisionActionKey = "provision-hosted-runner";

    public async Task HandleAsync(ManagedRunnerProvisionRequestedEvent message, CancellationToken cancellationToken = default)
    {
        if (message.OperationId == Guid.Empty)
        {
            logger.LogWarning("Managed runner provisioning request has no operation id and was rejected");
            return;
        }

        var hostedRunner = await dbContext.HostedRunners
            .Include(x => x.RunnerHost)
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x => x.Id == message.HostedRunnerId && x.DeletedAt == null, cancellationToken);

        if (hostedRunner == null)
        {
            logger.LogWarning("Managed runner provisioning skipped because hosted runner {HostedRunnerId} no longer exists", message.HostedRunnerId);
            return;
        }

        var deployment = hostedRunner.Deployments.FirstOrDefault(x => x.Id == message.OperationId);
        if (deployment == null)
        {
            logger.LogWarning("Managed runner provisioning operation {OperationId} no longer exists", message.OperationId);
            return;
        }

        if (!await TryClaimDispatchAsync(deployment, cancellationToken))
        {
            logger.LogInformation("Ignoring duplicate managed runner provisioning request for operation {OperationId} in state {Status}", deployment.Id, deployment.Status);
            return;
        }

        var host = hostedRunner.RunnerHost;
        if (host?.InfrastructureRunnerId == null)
        {
            await FailAsync(hostedRunner, deployment, "The selected Runner Host has no Infrastructure Runner assigned.", message.CorrelationId, cancellationToken);
            return;
        }

        if (!string.Equals(host.ProvisioningTransport, RunnerHostProvisioningTransports.InfrastructureRunner, StringComparison.Ordinal))
        {
            await FailAsync(hostedRunner, deployment, "Normal hosted Runner provisioning requires an Infrastructure Runner transport.", message.CorrelationId, cancellationToken);
            return;
        }

        if (!options.Value.Enabled)
        {
            logger.LogInformation("Managed runner provisioning skipped for hosted runner {HostedRunnerId} because HostedRunners:Enabled=false", hostedRunner.Id);
            return;
        }

        if (message.SecretGrantIds.Count == 0)
        {
            await FailAsync(hostedRunner, deployment, "The hosted Runner provisioning request has no enrollment credential grant.", message.CorrelationId, cancellationToken);
            return;
        }

        var resources = hostedRunner.PendingSpecificationSnapshot?.Resources ?? hostedRunner.SpecificationSnapshot.Resources;
        var input = JsonSerializer.Serialize(new
        {
            operationId = deployment.Id,
            hostedRunnerId = hostedRunner.Id,
            userId = hostedRunner.UserId,
            containerName = hostedRunner.ContainerName,
            image = options.Value.DockerImage,
            apiUrl = options.Value.ServerUrl,
            cpuLimit = resources.CpuLimit,
            memoryMb = resources.MemoryLimitMb,
            memoryReservationMb = resources.MemoryReservationMb,
            pidsLimit = resources.PidsLimit,
            publishedPort = hostedRunner.RuntimePort,
            runnerPort = options.Value.RuntimePort,
            networkName = options.Value.DockerNetwork,
            runtimeNetworkName = options.Value.RuntimeDockerNetwork,
            volumeName = $"automaton-runner-{hostedRunner.Id:N}-data",
            dataRoot = options.Value.DataRoot,
            runnerName = hostedRunner.Name
        });

        hostedRunner.Status = HostedRunnerStatuses.Provisioning;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        deployment.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(new InfrastructureActionExecutionRequestedEvent
        {
            OperationId = deployment.Id,
            HostedRunnerId = hostedRunner.Id,
            UserId = hostedRunner.UserId,
            RunnerHostId = host.Id,
            ActionKey = deployment.Action == HostedRunnerDeploymentActions.Reprovision
                ? "reprovision-hosted-runner"
                : ProvisionActionKey,
            InputJson = input,
            SecretGrantIds = message.SecretGrantIds,
            CorrelationId = message.CorrelationId
        }, cancellationToken);
    }

    private async Task FailAsync(
        Automaton.Api.Entities.HostedRunner hostedRunner,
        HostedRunnerDeployment deployment,
        string error,
        string correlationId,
        CancellationToken cancellationToken)
    {
        hostedRunner.Status = HostedRunnerStatuses.Failed;
        hostedRunner.LastError = error;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        deployment.Status = HostedRunnerDeploymentStatuses.Failed;
        deployment.Error = error;
        deployment.FinishedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAsync(new ManagedRunnerProvisionFailedEvent
        {
            HostedRunnerId = hostedRunner.Id,
            UserId = hostedRunner.UserId,
            Error = error,
            CorrelationId = correlationId
        }, cancellationToken);
    }

    private async Task<bool> TryClaimDispatchAsync(HostedRunnerDeployment deployment, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var affected = await dbContext.HostedRunnerDeployments
                .Where(x => x.Id == deployment.Id && x.Status == HostedRunnerDeploymentStatuses.Queued)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Dispatching),
                    cancellationToken);
            if (affected != 1) return false;
        }
        else if (deployment.Status != HostedRunnerDeploymentStatuses.Queued)
        {
            return false;
        }

        deployment.Status = HostedRunnerDeploymentStatuses.Dispatching;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
