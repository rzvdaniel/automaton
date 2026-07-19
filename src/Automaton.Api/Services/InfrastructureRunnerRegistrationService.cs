using System.Data;
using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class InfrastructureRunnerRegistrationService(
    ApplicationDbContext db,
    ApiKeyService apiKeyService,
    IAuditEventService auditEventService,
    ILogger<InfrastructureRunnerRegistrationService> logger)
{
    public async Task<InfrastructureRunnerRegistrationResponse?> RegisterAsync(
        string enrollmentApiKey,
        InfrastructureRunnerRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var response = await RegisterCoreAsync(enrollmentApiKey, request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }

        return await RegisterCoreAsync(enrollmentApiKey, request, cancellationToken);
    }

    private async Task<InfrastructureRunnerRegistrationResponse?> RegisterCoreAsync(
        string enrollmentApiKey,
        InfrastructureRunnerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var consumed = await apiKeyService.ConsumeApiKeyAsync(
            enrollmentApiKey,
            ApiKeyScope.InfrastructureRunnerEnroll,
            cancellationToken);
        if (consumed == null)
            return null;

        var ownerId = consumed.Value.UserId;
        var purpose = RunnerPurposes.Infrastructure;
        var capabilities = RunnerCapabilities.Normalize(request.Capabilities, purpose);
        var now = DateTime.UtcNow;
        var existingRunner = await db.Runners
            .FirstOrDefaultAsync(x => x.InstallationId == request.InstallationId, cancellationToken);

        ApiKey permanentKey;
        string rawPermanentKey;
        Runner runner;
        RunnerHost host;

        if (existingRunner != null)
        {
            if (!string.Equals(existingRunner.Purpose, purpose, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The installation ID belongs to a non-infrastructure Runner.");

            host = await db.RunnerHosts
                .FirstOrDefaultAsync(x => x.InfrastructureRunnerId == existingRunner.Id, cancellationToken)
                ?? throw new InvalidOperationException("The infrastructure Runner is not linked to a host.");

            (rawPermanentKey, permanentKey) = await apiKeyService.CreateApiKeyAsync(
                ownerId,
                $"Infrastructure Runner - {request.RunnerName}"[..Math.Min(100, $"Infrastructure Runner - {request.RunnerName}".Length)],
                ApiKeyScope.Runner,
                "Issued by Infrastructure Runner self-registration.",
                saveChanges: false,
                source: ApiKeySource.InfrastructureRunnerRegistration);

            if (existingRunner.ApiKeyId.HasValue)
            {
                var oldKey = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == existingRunner.ApiKeyId.Value, cancellationToken);
                if (oldKey != null)
                    oldKey.IsActive = false;
            }

            existingRunner.ApiKeyId = permanentKey.Id;
            runner = existingRunner;
        }
        else
        {
            (rawPermanentKey, permanentKey) = await apiKeyService.CreateApiKeyAsync(
                ownerId,
                $"Infrastructure Runner - {request.RunnerName}"[..Math.Min(100, $"Infrastructure Runner - {request.RunnerName}".Length)],
                ApiKeyScope.Runner,
                "Issued by Infrastructure Runner self-registration.",
                saveChanges: false,
                source: ApiKeySource.InfrastructureRunnerRegistration);

            runner = new Runner
            {
                Id = Guid.NewGuid(),
                Name = request.RunnerName,
                Purpose = purpose,
                ApiKeyId = permanentKey.Id,
                InstallationId = request.InstallationId
            };

            host = new RunnerHost
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(request.MachineName) ? request.RunnerName : request.MachineName,
                Provider = string.IsNullOrWhiteSpace(request.Provider) ? "unknown" : request.Provider,
                Region = request.Region,
                PublicIp = request.PublicIp,
                PrivateIp = request.PrivateIp,
                ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
                RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort,
                HostedRunnerSchedulingEnabled = false,
                Status = RunnerHostStatuses.Active,
                ProvisioningStatus = RunnerHostProvisioningStatuses.Validating,
                ProvisioningStartedAtUtc = now,
                InfrastructureRunnerEnrolledAtUtc = now,
                TotalCpuCores = request.CpuCores.GetValueOrDefault(2m),
                TotalMemoryMb = request.MemoryMb.GetValueOrDefault(4096),
                TotalDiskMb = request.DiskMb.GetValueOrDefault(40960),
                LastHeartbeatAtUtc = now,
                InfrastructureRunnerId = runner.Id,
                MetadataJson = BuildMetadataJson(request, now),
                CreatedAt = now
            };

            db.Runners.Add(runner);
            db.RunnerHosts.Add(host);
            db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = ownerId });
        }

        runner.Name = request.RunnerName;
        runner.Version = request.RunnerVersion;
        runner.MachineName = request.MachineName;
        runner.IpAddress = request.PublicIp ?? request.PrivateIp;
        runner.Purpose = purpose;
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, purpose);
        runner.InstallationId = request.InstallationId;
        host.PublicIp = request.PublicIp ?? host.PublicIp;
        host.PrivateIp = request.PrivateIp ?? host.PrivateIp;
        host.TotalCpuCores = request.CpuCores.GetValueOrDefault(host.TotalCpuCores);
        host.TotalMemoryMb = request.MemoryMb.GetValueOrDefault(host.TotalMemoryMb);
        host.TotalDiskMb = request.DiskMb.GetValueOrDefault(host.TotalDiskMb);
        host.LastHeartbeatAtUtc = now;
        host.MetadataJson = BuildMetadataJson(request, now);
        host.ProvisioningStatus = host.ProvisioningStatus is RunnerHostProvisioningStatuses.Ready
            or RunnerHostProvisioningStatuses.AwaitingApproval
            ? host.ProvisioningStatus
            : RunnerHostProvisioningStatuses.Validating;
        host.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.Runner,
            EntityId = runner.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Infrastructure Runner '{runner.Name}' self-registered.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = ownerId,
            Metadata = new { HostId = host.Id, RunnerId = runner.Id, runner.InstallationId, runner.Purpose, capabilities }
        }, cancellationToken);

        logger.LogInformation("Infrastructure Runner {RunnerId} self-registered host {RunnerHostId}", runner.Id, host.Id);
        return new InfrastructureRunnerRegistrationResponse
        {
            RunnerId = runner.Id,
            RunnerHostId = host.Id,
            RunnerName = runner.Name,
            ApiKey = rawPermanentKey,
            Purpose = purpose,
            Capabilities = capabilities,
            InstallationId = request.InstallationId
        };
    }

    private static void ValidateRequest(InfrastructureRunnerRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InstallationId) || request.InstallationId.Length > 128)
            throw new ArgumentException("A valid persistent installation ID is required.");
        if (string.IsNullOrWhiteSpace(request.RunnerName) || request.RunnerName.Length > 120)
            throw new ArgumentException("A valid Runner name is required.");
    }

    private static string BuildMetadataJson(InfrastructureRunnerRegistrationRequest request, DateTime timestamp)
        => JsonSerializer.Serialize(new
        {
            hostname = request.MachineName,
            operatingSystem = request.OperatingSystem,
            architecture = request.Architecture,
            dockerVersion = request.DockerVersion,
            runnerVersion = request.RunnerVersion,
            provider = request.Provider,
            region = request.Region,
            reportedAtUtc = timestamp
        });
}
