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
    UserContextService userContextService,
    IAuditEventService auditEventService,
    SelfHostedRunnerEnrollmentExchangeService enrollmentExchangeService,
    IOptions<RunnerEnrollmentOptions> enrollmentOptions,
    IOptions<HostedRunnerOptions> hostedRunnerOptions)
{
    private static readonly string HostAccessWarning =
        "Privileged Host applications may read and modify host files, inspect host processes, " +
        "inspect and control Docker containers, affect services and system state, and access " +
        "secrets available on the host.";
    private const string MachineManagementWarning =
        "The management Runner receives access to this machine's Docker socket. App deployments and Actions run in bounded containers; raw workflows and custom steps that execute code in the management process remain disabled. The Runner itself is trusted infrastructure: a compromise could control host Docker. Keep ordinary app, build and Action code in isolated containers.";

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
        if (profile != RunnerApplicationProfiles.MachineManagement)
            throw new ArgumentException("Only Machine Management Runners can be installed.");
        if (profile == RunnerApplicationProfiles.MachineManagement && !request.AcknowledgeHostAccess)
            throw new UnauthorizedAccessException("Host access must be explicitly acknowledged for this Runner profile.");
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
            RequiresHostAccessAcknowledgement = profile is RunnerApplicationProfiles.PrivilegedHost or RunnerApplicationProfiles.MachineManagement,
            HostAccessWarning = profile switch
            {
                RunnerApplicationProfiles.PrivilegedHost => HostAccessWarning,
                RunnerApplicationProfiles.MachineManagement => MachineManagementWarning,
                _ => null
            },
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

    public Task<RunnerEnrollmentExchangeResponse?> ExchangeAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken) =>
        enrollmentExchangeService.ExchangeAsync(request, cancellationToken);

    private string BuildCommand(SelfHostedRunnerEnrollment enrollment, string token)
    {
        var root = $"/opt/automaton/self-hosted-runners/{enrollment.InstallationId}";
        var workspace = $"{root}/workspace";
        var container = $"automaton-self-hosted-{enrollment.InstallationId[3..15]}";
        var isPrivilegedHost = enrollment.ApplicationProfile == RunnerApplicationProfiles.PrivilegedHost;
        var isMachineManagement = enrollment.ApplicationProfile == RunnerApplicationProfiles.MachineManagement;
        var requiresRuntimeNetwork = isPrivilegedHost || isMachineManagement;
        var machineWorkspaceVolume = $"automaton-machine-runner-{enrollment.InstallationId}-workspace";
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
            (isMachineManagement ? "--group-add \"$docker_socket_gid\"" : string.Empty),
            (requiresRuntimeNetwork ? "--network \"$runtime_network\"" : string.Empty),
            (requiresRuntimeNetwork ? "-e AUTOMATON_RUNTIME_HOST_ADDRESS=\"$runtime_host_address\"" : string.Empty),
            (isMachineManagement ? "-e AUTOMATON_RUNNER_CAPABILITIES=ApplicationHosting,WorkflowExecution,ManagedContainerExecution" : string.Empty),
            (isMachineManagement ? $"-e AUTOMATON_RUNNER_INSTALLATION_ID={Quote(enrollment.InstallationId)} -e AUTOMATON_WORKSPACE_ROOT='/automaton/storage/workspace' -v \"$machine_workspace_volume:/automaton/storage\"" : string.Empty),
            "-e \"AUTOMATON_RUNNER_IMAGE=$resolved_image\"",
            "-e \"AUTOMATON_RUNNER_IMAGE_DIGEST=$resolved_image\"",
            $"-v {Quote(root + ":/app/config")}",
            workspaceArgs,
            (isMachineManagement ? "-v /var/run/docker.sock:/var/run/docker.sock" : string.Empty),
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
            BuildMachineHostPreflightCommands(isMachineManagement),
            "docker pull \"$runner_image\"",
            "if ! resolved_image=$(docker image inspect --format '{{index .RepoDigests 0}}' \"$runner_image\"); then",
            "  echo 'The Runner image digest could not be resolved after pulling the image.' >&2",
            "  exit 1",
            "fi",
            "case \"$resolved_image\" in",
            "  *@sha256:*) ;;",
            "  *) echo 'The Runner image did not resolve to an immutable repository digest.' >&2; exit 1 ;;",
            "esac",
            BuildMachineWorkspaceSetupCommands(isMachineManagement, machineWorkspaceVolume, enrollment.InstallationId, root),
            BuildNetworkSetupCommands(requiresRuntimeNetwork, isPrivilegedHost, network, root, workspace),
            $"if ! container_id=$({dockerRun}); then",
            "  echo \"Runner container $container_name could not be created or started.\" >&2",
            BuildNetworkCleanupCommand(requiresRuntimeNetwork),
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

    private static string BuildMachineHostPreflightCommands(bool isMachineManagement)
        => isMachineManagement
            ? string.Join('\n',
                "if [ \"$(uname -s)\" != 'Linux' ] || [ ! -r /etc/os-release ]; then",
                "  echo 'Machine Management installation requires an Ubuntu Linux host.' >&2",
                "  exit 1",
                "fi",
                ". /etc/os-release",
                "if [ \"${ID:-}\" != 'ubuntu' ]; then",
                "  echo 'Machine Management installation currently supports Ubuntu. Use an Ubuntu host and retry.' >&2",
                "  exit 1",
                "fi")
            : string.Empty;

    private static string BuildMachineWorkspaceSetupCommands(
        bool isMachineManagement,
        string volumeName,
        string installationId,
        string configDirectory)
        => isMachineManagement
            ? string.Join('\n',
                $"machine_workspace_volume={Quote(volumeName)}",
                "if ! docker volume create --label automaton.managed=true --label automaton.resource=machine-workspace-volume " +
                $"--label automaton.installation-id={Quote(installationId)} --label automaton.storage-scope=workspace \"$machine_workspace_volume\" >/dev/null; then",
                "  echo 'The shared Machine Management workspace volume could not be created.' >&2",
                "  exit 1",
                "fi",
                "if ! docker run --rm --user 0:0 " +
                $"-v {Quote(configDirectory + ":/app/config")} --entrypoint chown \"$resolved_image\" automaton:automaton /app/config; then",
                "  echo 'The Machine Management Runner config directory could not be prepared.' >&2",
                "  exit 1",
                "fi",
                "if ! docker_socket_gid=$(stat -c '%g' /var/run/docker.sock) || [ -z \"$docker_socket_gid\" ]; then",
                "  echo 'The Docker socket group could not be resolved for the Machine Management Runner.' >&2",
                "  exit 1",
                "fi",
                "case \"$docker_socket_gid\" in *[!0-9]*) echo 'The Docker socket group is invalid.' >&2; exit 1 ;; esac")
            : string.Empty;

    private static string BuildNetworkSetupCommands(
        bool requiresRuntimeNetwork,
        bool createPrivilegedWorkspace,
        string network,
        string root,
        string workspace)
    {
        if (!requiresRuntimeNetwork) return string.Empty;

        var commands = new List<string>
        {
            $"runtime_network={Quote(network)}",
            "if ! docker network create \"$runtime_network\" >/dev/null; then",
            "  echo 'The private Runner runtime network could not be created.' >&2",
            "  exit 1",
            "fi",
            "if ! runtime_host_address=$(docker network inspect --format '{{(index .IPAM.Config 0).Gateway}}' \"$runtime_network\") || [ -z \"$runtime_host_address\" ]; then",
            "  echo 'The private Runner runtime network gateway could not be resolved.' >&2",
            "  docker network rm \"$runtime_network\" >/dev/null 2>&1 || true",
            "  exit 1",
            "fi"
        };
        if (createPrivilegedWorkspace) commands.Add($"mkdir -p {Quote(root)} {Quote(workspace)}");
        return string.Join('\n', commands);
    }

    private static string BuildNetworkCleanupCommand(bool requiresRuntimeNetwork)
        => requiresRuntimeNetwork
            ? "  docker network rm \"$runtime_network\" >/dev/null 2>&1 || true"
            : string.Empty;

    private static string GenerateToken() => $"re_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static string Quote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
