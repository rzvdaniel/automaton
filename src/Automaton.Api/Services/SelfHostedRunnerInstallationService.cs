using System.Data;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Infrastructure.RunnerHosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class SelfHostedRunnerInstallationService(
    ApplicationDbContext db,
    ApiKeyService apiKeyService,
    UserContextService userContextService,
    IAuditEventService auditEventService,
    IOptions<RunnerEnrollmentOptions> enrollmentOptions,
    IOptions<HostedRunnerOptions> hostedRunnerOptions,
    ILogger<SelfHostedRunnerInstallationService> logger)
{
    private static readonly string HostAccessWarning =
        "Privileged Host applications may read and modify host files, inspect host processes, " +
        "inspect and control Docker containers, affect services and system state, and access " +
        "secrets available on the host.";

    public async Task<SelfHostedRunnerInstallationResponse> CreateAsync(
        CreateSelfHostedRunnerInstallationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user context is required.");

        var runnerName = request.RunnerName?.Trim() ?? string.Empty;
        if (runnerName.Length is < 1 or > 120)
            throw new ArgumentException("RunnerName must be between 1 and 120 characters.");

        var profile = RunnerApplicationProfiles.Normalize(request.ApplicationProfile);
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddMinutes(Math.Clamp(enrollmentOptions.Value.TokenLifetimeMinutes, 1, 60));
        var token = GenerateToken();
        var installationId = $"sh_{Guid.NewGuid():N}";
        var enrollment = new SelfHostedRunnerEnrollment
        {
            Id = Guid.NewGuid(), UserId = userId, RunnerName = runnerName,
            ApplicationProfile = profile, EnrollmentTokenHash = HashToken(token),
            InstallationId = installationId, CreatedAtUtc = now, ExpiresAtUtc = expiresAtUtc
        };

        db.SelfHostedRunnerEnrollments.Add(enrollment);
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created, EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id, EventType = AuditEventTypes.RunnerEnrollmentCreated,
            Message = $"Self-hosted Runner installation created for '{runnerName}'.",
            Outcome = AuditOutcomes.Success, ActorUserId = userId,
            Metadata = new { enrollment.RunnerName, enrollment.ApplicationProfile, enrollment.ExpiresAtUtc }
        }, cancellationToken);

        return new SelfHostedRunnerInstallationResponse
        {
            InstallationId = enrollment.InstallationId,
            EnrollmentId = enrollment.Id,
            RunnerName = runnerName,
            ApplicationProfile = profile,
            ExpiresAtUtc = expiresAtUtc,
            InstallationCommand = BuildCommand(enrollment, token),
            RequiresHostAccessAcknowledgement = profile == RunnerApplicationProfiles.PrivilegedHost,
            HostAccessWarning = profile == RunnerApplicationProfiles.PrivilegedHost ? HostAccessWarning : null,
            Status = SelfHostedRunnerInstallationStatuses.CommandCreated
        };
    }

    public async Task<SelfHostedRunnerInstallationStatusResponse?> GetStatusAsync(
        string installationId,
        CancellationToken cancellationToken)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user context is required.");

        var enrollment = await db.SelfHostedRunnerEnrollments
            .FirstOrDefaultAsync(
                x => x.InstallationId == installationId && x.UserId == userId,
                cancellationToken);
        if (enrollment == null) return null;

        var now = DateTime.UtcNow;
        if (enrollment.Status == RunnerEnrollmentStatuses.Pending && enrollment.ExpiresAtUtc <= now)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Expired;
            await db.SaveChangesAsync(cancellationToken);
        }

        Runner? runner = null;
        if (enrollment.RunnerId.HasValue)
        {
            runner = await db.Runners
                .FirstOrDefaultAsync(x => x.Id == enrollment.RunnerId.Value, cancellationToken);
        }

        var status = enrollment.Status switch
        {
            RunnerEnrollmentStatuses.Expired => SelfHostedRunnerInstallationStatuses.Expired,
            RunnerEnrollmentStatuses.Completed when runner != null &&
                RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, now)
                => SelfHostedRunnerInstallationStatuses.RunnerConnected,
            RunnerEnrollmentStatuses.Completed when runner?.LastConnectedAtUtc != null
                => SelfHostedRunnerInstallationStatuses.OfflineAfterEnrollment,
            RunnerEnrollmentStatuses.Completed => SelfHostedRunnerInstallationStatuses.RunnerEnrolled,
            _ => SelfHostedRunnerInstallationStatuses.WaitingForEnrollment
        };

        return new SelfHostedRunnerInstallationStatusResponse
        {
            InstallationId = enrollment.InstallationId,
            Status = status,
            ExpiresAtUtc = enrollment.ExpiresAtUtc,
            RunnerId = runner?.Id,
            LastConnectedAtUtc = runner?.LastConnectedAtUtc,
            LastHeartbeatAtUtc = runner?.LastHeartbeatAtUtc
        };
    }

    public async Task<RunnerEnrollmentExchangeResponse?> ExchangeAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentToken)) return null;
        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var response = await ExchangeCoreAsync(request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        return await ExchangeCoreAsync(request, cancellationToken);
    }

    private async Task<RunnerEnrollmentExchangeResponse?> ExchangeCoreAsync(
        RunnerEnrollmentExchangeRequest request, CancellationToken cancellationToken)
    {
        var hash = HashToken(request.EnrollmentToken.Trim());
        var enrollment = await db.SelfHostedRunnerEnrollments
            .FirstOrDefaultAsync(x => x.EnrollmentTokenHash == hash, cancellationToken);
        if (enrollment == null || enrollment.Status != RunnerEnrollmentStatuses.Pending) return null;
        var now = DateTime.UtcNow;
        if (enrollment.ExpiresAtUtc <= now)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Expired;
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.RunnerName)
            && !string.Equals(request.RunnerName.Trim(), enrollment.RunnerName, StringComparison.Ordinal))
            throw new InvalidOperationException("The Runner name does not match the self-hosted installation enrollment.");

        var capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed);
        var existing = await db.Runners.Include(x => x.RunnerUsers)
            .FirstOrDefaultAsync(x => x.InstallationId == enrollment.InstallationId, cancellationToken);
        if (existing != null && (!existing.RunnerUsers.Any(x => x.UserId == enrollment.UserId)
            || !string.Equals(existing.Purpose, RunnerPurposes.Managed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The self-hosted installation identity belongs to another Runner.");

        var (rawApiKey, apiKey) = await apiKeyService.CreateApiKeyAsync(
            enrollment.UserId, $"Self-hosted Runner - {enrollment.RunnerName}", ApiKeyScope.Runner,
            "Issued after one-time self-hosted Runner enrollment.", saveChanges: false,
            source: ApiKeySource.RunnerProvisioning);
        var runner = existing ?? new Runner { Id = Guid.NewGuid(), InstallationId = enrollment.InstallationId };
        runner.Name = enrollment.RunnerName;
        runner.Status = RunnerConnectivityStatuses.Unknown;
        runner.Version = request.RunnerVersion;
        runner.MachineName = request.MachineName;
        runner.Purpose = RunnerPurposes.Managed;
        runner.ApplicationProfile = enrollment.ApplicationProfile;
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, RunnerPurposes.Managed);
        runner.ApiKeyId = apiKey.Id;
        if (existing == null)
        {
            db.Runners.Add(runner);
            db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = enrollment.UserId });
        }
        enrollment.Status = RunnerEnrollmentStatuses.Completed;
        enrollment.UsedAtUtc = now;
        enrollment.RunnerId = runner.Id;
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded, EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id, EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Self-hosted Runner '{runner.Name}' enrolled successfully.", Outcome = AuditOutcomes.Success,
            ActorUserId = enrollment.UserId,
            Metadata = new { runner.Id, runner.Name, runner.Purpose, runner.ApplicationProfile, Capabilities = capabilities }
        }, cancellationToken);
        logger.LogInformation("Self-hosted Runner {RunnerId} enrolled from installation {InstallationId}", runner.Id, enrollment.InstallationId);
        return new RunnerEnrollmentExchangeResponse
        {
            RunnerId = runner.Id, RunnerName = runner.Name, ApiKey = rawApiKey,
            Purpose = runner.Purpose, Capabilities = capabilities, InstallationId = runner.InstallationId!
        };
    }

    private string BuildCommand(SelfHostedRunnerEnrollment enrollment, string token)
    {
        var root = $"/opt/automaton/self-hosted-runners/{enrollment.InstallationId}";
        var workspace = $"{root}/workspace";
        var container = $"automaton-self-hosted-{enrollment.InstallationId[3..15]}";
        var isPrivilegedHost = enrollment.ApplicationProfile == RunnerApplicationProfiles.PrivilegedHost;
        var network = $"automaton-self-hosted-{enrollment.InstallationId}-runtime";
        var args = isPrivilegedHost
            ? $" {RunnerDockerArguments.PrivilegedHostApplication}"
            : string.Empty;
        var workspaceArgs = isPrivilegedHost
            ? string.Join(" ",
                $"-e AUTOMATON_WORKSPACE_ROOT={Quote("/automaton/workspace")}",
                $"-e AUTOMATON_HOST_WORKSPACE_ROOT={Quote(workspace)}",
                $"-v {Quote(workspace + ":/automaton/workspace")}")
            : string.Empty;
        var dockerRun = string.Join(" ",
            "docker run -d", $"--name {Quote(container)}", "--restart unless-stopped",
            $"-e AUTOMATON_SERVER_URL={Quote(hostedRunnerOptions.Value.ServerUrl.TrimEnd('/'))}",
            $"-e AUTOMATON_RUNNER_NAME={Quote(enrollment.RunnerName)}",
            $"-e AUTOMATON_RUNNER_PURPOSE={Quote(RunnerPurposes.Managed)}",
            $"-e AUTOMATON_RUNNER_APPLICATION_PROFILE={Quote(enrollment.ApplicationProfile)}",
            $"-e AUTOMATON_ENROLLMENT_TOKEN={Quote(token)}",
            (isPrivilegedHost ? "--network \"$runtime_network\"" : string.Empty),
            (isPrivilegedHost ? "-e AUTOMATON_RUNTIME_HOST_ADDRESS=\"$runtime_host_address\"" : string.Empty),
            "-e \"AUTOMATON_RUNNER_IMAGE=$resolved_image\"",
            "-e \"AUTOMATON_RUNNER_IMAGE_DIGEST=$resolved_image\"",
            $"-v {Quote(root + ":/app/config")}",
            workspaceArgs,
            args.Trim(),
            "\"$resolved_image\"");

        return string.Join('\n',
            "/bin/sh <<'AUTOMATON_RUNNER_INSTALL'",
            "set -eu",
            $"container_name={Quote(container)}",
            $"runner_image={Quote(hostedRunnerOptions.Value.DockerImage)}",
            "if ! command -v docker >/dev/null 2>&1; then",
            "  echo 'Docker is required but was not found.' >&2",
            "  exit 1",
            "fi",
            "if ! docker info >/dev/null 2>&1; then",
            "  echo 'Docker is installed but the daemon is unavailable.' >&2",
            "  exit 1",
            "fi",
            "docker pull \"$runner_image\"",
            "if ! resolved_image=$(docker image inspect --format '{{index .RepoDigests 0}}' \"$runner_image\"); then",
            "  echo 'The Runner image digest could not be resolved after pulling the image.' >&2",
            "  exit 1",
            "fi",
            "case \"$resolved_image\" in",
            "  *@sha256:*) ;;",
            "  *) echo 'The Runner image did not resolve to an immutable repository digest.' >&2; exit 1 ;;",
            "esac",
            BuildNetworkSetupCommands(isPrivilegedHost, network, root, workspace),
            $"if ! container_id=$({dockerRun}); then",
            "  echo \"Runner container $container_name could not be created or started.\" >&2",
            BuildNetworkCleanupCommand(isPrivilegedHost),
            "  exit 1",
            "fi",
            "short_id=$(printf '%s' \"$container_id\" | cut -c1-12)",
            "printf 'Runner container %s started (%s).\\n' \"$container_name\" \"$short_id\"",
            "sleep 5",
            "status=$(docker inspect --format '{{.State.Status}}' \"$container_name\" 2>/dev/null || true)",
            "case \"$status\" in",
            "  running)",
            "    echo 'Runner container is running.'",
            "    echo 'Waiting for it to enroll and connect to Automaton.'",
            "    ;;",
            "  restarting|exited|dead)",
            "    echo \"Runner container failed local startup (status: $status).\" >&2",
            "    docker logs --tail 80 \"$container_name\" 2>&1 | tail -n 80 | sed -E 's/(re|ak)_[A-Za-z0-9_-]+/[REDACTED]/g' >&2 || true",
            "    exit 1",
            "    ;;",
            "  *)",
            "    echo \"Runner container startup could not be confirmed (status: ${status:-missing}).\" >&2",
            "    docker logs --tail 80 \"$container_name\" 2>&1 | tail -n 80 | sed -E 's/(re|ak)_[A-Za-z0-9_-]+/[REDACTED]/g' >&2 || true",
            "    exit 1",
            "    ;;",
            "esac",
            "AUTOMATON_RUNNER_INSTALL");
    }

    private static string BuildNetworkSetupCommands(bool isPrivilegedHost, string network, string root, string workspace)
    {
        if (!isPrivilegedHost)
        {
            return string.Join('\n', new string[11]);
        }

        return string.Join('\n',
            $"runtime_network={Quote(network)}",
            "if ! docker network create \"$runtime_network\" >/dev/null; then",
            "  echo 'The private Runner runtime network could not be created.' >&2",
            "  exit 1",
            "fi",
            "if ! runtime_host_address=$(docker network inspect --format '{{(index .IPAM.Config 0).Gateway}}' \"$runtime_network\") || [ -z \"$runtime_host_address\" ]; then",
            "  echo 'The private Runner runtime network gateway could not be resolved.' >&2",
            "  docker network rm \"$runtime_network\" >/dev/null 2>&1 || true",
            "  exit 1",
            "fi",
            $"mkdir -p {Quote(root)} {Quote(workspace)}");
    }

    private static string BuildNetworkCleanupCommand(bool isPrivilegedHost)
        => isPrivilegedHost
            ? "  docker network rm \"$runtime_network\" >/dev/null 2>&1 || true"
            : string.Empty;

    private static string GenerateToken() => $"re_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static string Quote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
