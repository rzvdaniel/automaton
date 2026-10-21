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
    private sealed record RegistrationResult(Runner Runner, RunnerHost Host, string RawApiKey);

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

        var registration = existingRunner is null
            ? await CreateRunnerRegistrationAsync(ownerId, request, now)
            : await RefreshRunnerRegistrationAsync(existingRunner, ownerId, request, cancellationToken);

        UpdateReportedRunnerState(registration.Runner, registration.Host, request, capabilities, now, purpose);
        return await PersistRegistrationAsync(registration, ownerId, request, capabilities, purpose, cancellationToken);
    }

    private async Task<InfrastructureRunnerRegistrationResponse> PersistRegistrationAsync(
        RegistrationResult registration,
        Guid ownerId,
        InfrastructureRunnerRegistrationRequest request,
        IReadOnlyList<string> capabilities,
        string purpose,
        CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.Runner,
            EntityId = registration.Runner.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Infrastructure Runner '{registration.Runner.Name}' self-registered.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = ownerId,
            Metadata = new
            {
                HostId = registration.Host.Id,
                RunnerId = registration.Runner.Id,
                registration.Runner.InstallationId,
                registration.Runner.Purpose,
                capabilities
            }
        }, cancellationToken);

        logger.LogInformation(
            "Infrastructure Runner {RunnerId} self-registered host {RunnerHostId}",
            registration.Runner.Id,
            registration.Host.Id);
        return new InfrastructureRunnerRegistrationResponse
        {
            RunnerId = registration.Runner.Id,
            RunnerHostId = registration.Host.Id,
            RunnerName = registration.Runner.Name,
            ApiKey = registration.RawApiKey,
            Purpose = purpose,
            Capabilities = capabilities,
            InstallationId = request.InstallationId
        };
    }

    private async Task<RegistrationResult> CreateRunnerRegistrationAsync(
        Guid ownerId,
        InfrastructureRunnerRegistrationRequest request,
        DateTime now)
    {
        var (rawApiKey, permanentKey) = await CreatePermanentRunnerApiKeyAsync(ownerId, request.RunnerName);
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = request.RunnerName,
            Purpose = RunnerPurposes.Infrastructure,
            ApiKeyId = permanentKey.Id,
            InstallationId = request.InstallationId
        };
        var host = CreateRunnerHost(runner, request, now);

        db.Runners.Add(runner);
        db.RunnerHosts.Add(host);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = ownerId });
        return new RegistrationResult(runner, host, rawApiKey);
    }

    private async Task<RegistrationResult> RefreshRunnerRegistrationAsync(
        Runner runner,
        Guid ownerId,
        InfrastructureRunnerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The installation ID belongs to a non-infrastructure Runner.");

        var host = await db.RunnerHosts
            .FirstOrDefaultAsync(x => x.InfrastructureRunnerId == runner.Id, cancellationToken)
            ?? throw new InvalidOperationException("The infrastructure Runner is not linked to a host.");
        var (rawApiKey, permanentKey) = await CreatePermanentRunnerApiKeyAsync(ownerId, request.RunnerName);

        if (runner.ApiKeyId.HasValue)
        {
            var oldKey = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == runner.ApiKeyId.Value, cancellationToken);
            if (oldKey is not null)
                oldKey.IsActive = false;
        }

        runner.ApiKeyId = permanentKey.Id;
        return new RegistrationResult(runner, host, rawApiKey);
    }

    private Task<(string RawApiKey, ApiKey Entity)> CreatePermanentRunnerApiKeyAsync(Guid ownerId, string runnerName)
    {
        var keyName = $"Infrastructure Runner - {runnerName}";
        if (keyName.Length > 100)
            keyName = keyName[..100];

        return apiKeyService.CreateApiKeyAsync(
            ownerId,
            keyName,
            ApiKeyScope.Runner,
            "Issued by Infrastructure Runner self-registration.",
            saveChanges: false,
            source: ApiKeySource.InfrastructureRunnerRegistration);
    }

    private static RunnerHost CreateRunnerHost(
        Runner runner,
        InfrastructureRunnerRegistrationRequest request,
        DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(request.MachineName) ? request.RunnerName : request.MachineName,
            Provider = string.IsNullOrWhiteSpace(request.Provider) ? "unknown" : request.Provider,
            Region = request.Region,
            PublicIp = request.PublicIp,
            PrivateIp = request.PrivateIp,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort,
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

    private static void UpdateReportedRunnerState(
        Runner runner,
        RunnerHost host,
        InfrastructureRunnerRegistrationRequest request,
        IReadOnlyList<string> capabilities,
        DateTime now,
        string purpose)
    {
        runner.Name = request.RunnerName;
        runner.Version = request.RunnerVersion;
        runner.MachineName = request.MachineName;
        runner.IpAddress = request.PublicIp ?? request.PrivateIp;
        runner.Purpose = purpose;
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, purpose);
        runner.InstallationId = request.InstallationId;
        // An administrator-approved runtime address is authoritative. Runner
        // reports may fill an empty address, but reinstalling must not replace it.
        if (string.IsNullOrWhiteSpace(host.PublicIp) && !string.IsNullOrWhiteSpace(request.PublicIp))
            host.PublicIp = request.PublicIp;
        if (string.IsNullOrWhiteSpace(host.PrivateIp) && !string.IsNullOrWhiteSpace(request.PrivateIp))
            host.PrivateIp = request.PrivateIp;
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
