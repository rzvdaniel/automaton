using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace Automaton.Worker.Handlers;

public sealed class ManagedRunnerProvisionRequestedEventHandler(
    ApplicationDbContext dbContext,
    IEventPublisher eventPublisher,
    ITransactionalEventExecutor transactionalEventExecutor,
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

        if (!HostedRunnerImageReference.IsImmutable(options.Value.DockerImage))
        {
            const string error = "Hosted Runner provisioning requires an immutable container image reference in the form repository@sha256:<digest>.";
            await FailAsync(hostedRunner, deployment, error, message.CorrelationId, cancellationToken);
            return;
        }

        if (message.SecretGrantIds.Count == 0)
        {
            await FailAsync(hostedRunner, deployment, "The hosted Runner provisioning request has no enrollment credential grant.", message.CorrelationId, cancellationToken);
            return;
        }

        var resources = hostedRunner.PendingSpecificationSnapshot?.Resources ?? hostedRunner.SpecificationSnapshot.Resources;
        if (hostedRunner.StorageProjectId is not > 0)
        {
            await FailAsync(hostedRunner, deployment, "The Hosted Runner has no persisted storage project identity.", message.CorrelationId, cancellationToken);
            return;
        }
        var runtimeHostAddress = RequirePrivateRuntimeHostAddress(host.PrivateIp);
        var snapshotSpecificationId = hostedRunner.PendingSpecificationSnapshot?.SourceSpecificationId
            ?? hostedRunner.SpecificationSnapshot.SourceSpecificationId;
        var specificationId = snapshotSpecificationId == Guid.Empty
            ? hostedRunner.HostedRunnerSpecificationId
            : snapshotSpecificationId;
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
            diskLimitMb = resources.DiskLimitMb,
            xfsProjectId = hostedRunner.StorageProjectId.Value,
            pidsLimit = resources.PidsLimit,
            sharedMemoryMb = resources.SharedMemoryMb,
            dockerLogMaxSizeMb = resources.DockerLogMaxSizeMb,
            dockerLogMaxFiles = resources.DockerLogMaxFiles,
            publishedPort = hostedRunner.RuntimePort,
            runnerPort = options.Value.RuntimePort,
            networkName = options.Value.DockerNetwork,
            runtimeNetworkName = options.Value.RuntimeDockerNetwork,
            runtimeHostAddress,
            systemVolumeName = $"automaton-hosted-runner-{hostedRunner.Id:N}-system",
            userVolumeName = $"automaton-hosted-runner-{hostedRunner.Id:N}-user",
            specificationId,
            runnerName = hostedRunner.Name
        });

        var infrastructureRequest = new InfrastructureCommandRequestedEvent(
            CommandId: deployment.Id,
            OperationId: deployment.Id,
            RunnerHostId: host.Id,
            InfrastructureRunnerId: host.InfrastructureRunnerId.Value,
            ActionKey: deployment.Action == HostedRunnerDeploymentActions.Reprovision
                ? "reprovision-hosted-runner"
                : ProvisionActionKey,
            InputJson: input,
            SecretGrantIds: message.SecretGrantIds,
            CreatedAtUtc: DateTime.UtcNow);

        var claimed = false;
        await transactionalEventExecutor.ExecuteAsync(async transactionCancellationToken =>
        {
            claimed = await TryClaimDispatchAsync(deployment, transactionCancellationToken);
            if (!claimed) return;

            hostedRunner.Status = HostedRunnerStatuses.Provisioning;
            hostedRunner.LastError = null;
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            deployment.Error = null;
            await dbContext.SaveChangesAsync(transactionCancellationToken);
            await eventPublisher.PublishAsync(infrastructureRequest, transactionCancellationToken);
        }, cancellationToken);

        if (!claimed)
        {
            logger.LogInformation("Ignoring duplicate managed runner provisioning request for operation {OperationId} in state {Status}", deployment.Id, deployment.Status);
        }
    }

    private static string RequirePrivateRuntimeHostAddress(string? value)
    {
        if (!IPAddress.TryParse(value?.Trim(), out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || IPAddress.IsLoopback(address))
            throw new InvalidOperationException("The Runner Host requires a concrete private IPv4 runtime address.");
        var bytes = address.GetAddressBytes();
        if (!(bytes[0] == 10
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168))
            throw new InvalidOperationException("The Runner Host runtime address must use a private IPv4 range.");
        return address.ToString();
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
