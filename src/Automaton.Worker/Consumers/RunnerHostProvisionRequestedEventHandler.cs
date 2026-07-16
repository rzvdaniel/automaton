using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Infrastructure.Providers;
using Automaton.Infrastructure.RunnerHosts;
using Automaton.Messaging.Events;
using Automaton.Worker.Config;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Consumers;

public sealed class RunnerHostProvisionRequestedEventHandler(
    ApplicationDbContext db,
    IInfrastructureProviderResolver providerResolver,
    RunnerEnrollmentMaterialService enrollmentMaterialService,
    IOptions<AppConfig> appConfig,
    IOptions<HetznerInfrastructureProviderOptions> hetznerOptions,
    ILogger<RunnerHostProvisionRequestedEventHandler> logger)
{
    public async Task HandleAsync(RunnerHostProvisionRequestedEvent message, CancellationToken cancellationToken)
    {
        RunnerHostProvisioningOperation? operation;
        await using (var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            operation = await db.RunnerHostProvisioningOperations
                .Include(x => x.RunnerHost)
                .FirstOrDefaultAsync(x => x.Id == message.ProvisioningOperationId && x.RunnerHostId == message.RunnerHostId, cancellationToken);
            var now = DateTime.UtcNow;
            if (operation == null
                || operation.Status is RunnerHostProvisioningOperationStatuses.Succeeded or RunnerHostProvisioningOperationStatuses.Cancelled
                || (operation.Status == RunnerHostProvisioningOperationStatuses.Running
                    && operation.ClaimedUntilUtc > now))
                return;
            if (operation.RunnerHost.Status == RunnerHostStatuses.Disabled || operation.RunnerHost.Status == RunnerHostStatuses.Draining)
                return;

            operation.Status = RunnerHostProvisioningOperationStatuses.Running;
            operation.Attempt++;
            operation.StartedAtUtc = now;
            operation.ClaimedUntilUtc = now.AddMinutes(30);
            operation.RunnerHost.ProvisioningStatus = RunnerHostProvisioningStatuses.CreatingServer;
            operation.RunnerHost.HostedRunnerSchedulingEnabled = false;
            operation.RunnerHost.ProvisioningStartedAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var host = operation!.RunnerHost;
        IRunnerHostInfrastructureProvider provider;
        try
        {
            provider = providerResolver.Resolve(host.Provider);
            var existing = await RecoverExistingServerAsync(provider, host, cancellationToken);
            if (existing != null)
            {
                await PersistServerAsync(operation.Id, host.Id, existing, cancellationToken);
                return;
            }

            if (operation.EnrollmentId.HasValue)
                throw new InvalidOperationException("Provisioning resumed without plaintext enrollment material; use Recreate server or Manual enrollment.");

            var material = await enrollmentMaterialService.CreateAsync(host.Id, operation.RequestedByUserId, TimeSpan.FromMinutes(30), cancellationToken);
            var options = hetznerOptions.Value;
            var apiUrl = new Uri(appConfig.Value.ApiUrl.TrimEnd('/'));
            var cloudInit = RunnerHostCloudInit.Generate(new RunnerHostCloudInitRequest(apiUrl, host.Id, material.PlaintextToken, options.InfrastructureRunnerImage));
            operation.EnrollmentId = material.Enrollment.Id;
            operation.NextTimeoutAtUtc = material.Enrollment.ExpiresAtUtc;
            operation.CloudInitTemplateVersion = "infrastructure-runner.v1";
            operation.CloudInitGeneratedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var result = await provider.CreateServerAsync(new CreateRunnerHostServerRequest(
                $"automaton-runner-host-{host.Id:N}",
                host.Region ?? options.DefaultLocation,
                host.ProviderServerType ?? options.DefaultServerType,
                host.ProviderImageId ?? options.DockerImage,
                options.SshKeyIds,
                cloudInit,
                options.PrivateNetworkId,
                new Dictionary<string, string>
                {
                    ["automaton.runner-host-id"] = host.Id.ToString(),
                    ["automaton.environment"] = "production",
                    ["automaton.managed"] = "true"
                }), cancellationToken);
            await PersistServerAsync(operation.Id, host.Id, new RunnerHostServerInfo(result.ProviderResourceId, "running", result.PublicIp, result.PrivateIp, result.ProviderRegion, result.ProviderServerType, result.ProviderImageId), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Runner host provisioning failed for operation {OperationId}", operation.Id);
            await MarkFailedAsync(operation.Id, host.Id, "ProviderProvisioningFailed", ex.Message, cancellationToken);
            throw;
        }
    }

    private async Task<RunnerHostServerInfo?> RecoverExistingServerAsync(IRunnerHostInfrastructureProvider provider, RunnerHost host, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(host.ProviderResourceId))
            return await provider.GetServerAsync(host.ProviderResourceId, cancellationToken);
        var matches = await provider.FindServersByLabelAsync("automaton.runner-host-id", host.Id.ToString(), cancellationToken);
        if (matches.Count > 1) throw new InvalidOperationException("Multiple provider servers match this Runner Host.");
        return matches.SingleOrDefault();
    }

    private async Task PersistServerAsync(Guid operationId, Guid hostId, RunnerHostServerInfo server, CancellationToken cancellationToken)
    {
        var operation = await db.RunnerHostProvisioningOperations.FirstAsync(x => x.Id == operationId, cancellationToken);
        var host = await db.RunnerHosts.FirstAsync(x => x.Id == hostId, cancellationToken);
        host.ProviderResourceId = server.ProviderResourceId;
        host.PublicIp = server.PublicIp;
        host.PrivateIp = server.PrivateIp;
        host.Region = server.ProviderRegion ?? host.Region;
        host.ProviderServerType = server.ProviderServerType ?? host.ProviderServerType;
        host.ProviderImageId = server.ProviderImageId ?? host.ProviderImageId;
        host.ProvisioningStatus = RunnerHostProvisioningStatuses.AwaitingInfrastructureRunner;
        operation.ProviderResourceId = server.ProviderResourceId;
        operation.Status = RunnerHostProvisioningOperationStatuses.AwaitingInfrastructureRunner;
        operation.ClaimedUntilUtc = null;
        operation.FinishedAtUtc = DateTime.UtcNow;
        operation.ClaimedUntilUtc = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(Guid operationId, Guid hostId, string code, string message, CancellationToken cancellationToken)
    {
        var operation = await db.RunnerHostProvisioningOperations.FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken);
        var host = await db.RunnerHosts.FirstOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        if (operation == null || host == null) return;
        operation.Status = RunnerHostProvisioningOperationStatuses.Failed;
        operation.FailureCode = code;
        operation.Error = message.Length > 2000 ? message[..2000] : message;
        operation.FinishedAtUtc = DateTime.UtcNow;
        host.ProvisioningStatus = RunnerHostProvisioningStatuses.Failed;
        host.ProvisioningFailureCode = code;
        host.ProvisioningFailureMessage = operation.Error;
        host.HostedRunnerSchedulingEnabled = false;
        await db.SaveChangesAsync(cancellationToken);
    }
}
