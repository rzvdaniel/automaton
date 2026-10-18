using System.Security.Claims;
using System.Text.RegularExpressions;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public sealed class SelfHostedRunnerInstallationServiceTests
{
    [Test]
    public async Task Create_StoresOnlyHashAndBuildsIsolatedStandardCommand()
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var userId = Guid.NewGuid();
        var service = SelfHostedRunnerInstallationTestServices.CreateService(db, userId);

        var response = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "standard-self-hosted",
            ApplicationProfile = "standard"
        }, CancellationToken.None);

        var enrollment = await db.SelfHostedRunnerEnrollments.SingleAsync();
        Assert.That(response.ApplicationProfile, Is.EqualTo(RunnerApplicationProfiles.Standard));
        Assert.That(response.InstallationCommand, Does.Contain("automaton/runner"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("--privileged"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("/var/run/docker.sock"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("--network host"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("-p 8001"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("-p 8080"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("AUTOMATON_HOST_WORKSPACE_ROOT"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("/automaton/workspace"));
        Assert.That(enrollment.EnrollmentTokenHash, Does.Not.Contain("AUTOMATON_ENROLLMENT_TOKEN"));
        Assert.That(enrollment.EnrollmentTokenHash, Is.Not.EqualTo(string.Empty));
        Assert.That(enrollment.UserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task PrivilegedInstallation_UsesHostArgumentsButRemainsApplicationRunner()
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var service = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());

        var response = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "privileged-self-hosted",
            ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost
        }, CancellationToken.None);

        Assert.That(response.RequiresHostAccessAcknowledgement, Is.True);
        Assert.That(response.HostAccessWarning, Does.Contain("host files"));
        Assert.That(response.InstallationCommand, Does.Contain("--privileged"));
        Assert.That(response.InstallationCommand, Does.Contain("--pid host"));
        Assert.That(response.InstallationCommand, Does.Contain("/var/run/docker.sock:/var/run/docker.sock"));
        Assert.That(response.InstallationCommand, Does.Contain("-v /:/host"));
        Assert.That(response.InstallationCommand, Does.Contain(
            $"runtime_network='automaton-self-hosted-{response.InstallationId}-runtime'"));
        Assert.That(response.InstallationCommand, Does.Contain(
            "docker network create \"$runtime_network\""));
        Assert.That(response.InstallationCommand, Does.Contain(
            "docker network inspect --format '{{(index .IPAM.Config 0).Gateway}}' \"$runtime_network\""));
        Assert.That(response.InstallationCommand, Does.Contain(
            "--network \"$runtime_network\""));
        Assert.That(response.InstallationCommand, Does.Contain(
            "AUTOMATON_RUNTIME_HOST_ADDRESS=\"$runtime_host_address\""));
        Assert.That(response.InstallationCommand, Does.Not.Contain("AUTOMATON_RUNTIME_PUBLISH_ADDRESS"));
        Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_WORKSPACE_ROOT='/automaton/workspace'"));
        Assert.That(response.InstallationCommand, Does.Contain(
            $"AUTOMATON_HOST_WORKSPACE_ROOT='/opt/automaton/self-hosted-runners/{response.InstallationId}/workspace'"));
        Assert.That(response.InstallationCommand, Does.Contain(
            $"-v '/opt/automaton/self-hosted-runners/{response.InstallationId}/workspace:/automaton/workspace'"));
        Assert.That(response.InstallationCommand, Does.Contain(
            $"mkdir -p '/opt/automaton/self-hosted-runners/{response.InstallationId}' '/opt/automaton/self-hosted-runners/{response.InstallationId}/workspace'"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("--network host"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("-p 8001"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("-p 8080"));
        Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_RUNNER_PURPOSE='Managed'"));
        Assert.That(response.InstallationCommand, Does.Contain("docker image inspect --format '{{index .RepoDigests 0}}' \"$runner_image\""));
        Assert.That(response.InstallationCommand, Does.Contain("case \"$resolved_image\" in"));
        Assert.That(response.InstallationCommand, Does.Contain("@sha256:"));
        Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_RUNNER_IMAGE=$resolved_image"));
        Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_RUNNER_IMAGE_DIGEST=$resolved_image"));
        Assert.That(response.InstallationCommand, Does.Contain("\"$resolved_image\""));
        Assert.That(response.InstallationCommand, Does.Not.Contain("RUNNER_HOST_ID"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("HOSTED_RUNNER_ID"));
    }

    [Test]
    public async Task MachineManagementInstallation_RequiresAcknowledgementAndGrantsOnlyTypedContainerAccess()
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var service = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());
        var request = new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "machine-management",
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement
        };

        Assert.That(async () => await service.CreateAsync(request, CancellationToken.None),
            Throws.TypeOf<UnauthorizedAccessException>());

        var response = await service.CreateAsync(request with { AcknowledgeHostAccess = true }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.RequiresHostAccessAcknowledgement, Is.True);
            Assert.That(response.HostAccessWarning, Does.Contain("Docker socket"));
            Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_RUNNER_CAPABILITIES=ApplicationHosting,ManagedContainerExecution"));
            Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_WORKSPACE_ROOT='/automaton/storage/workspace'"));
            Assert.That(response.InstallationCommand, Does.Contain(
                $"machine_workspace_volume='automaton-machine-runner-{response.InstallationId}-workspace'"));
            Assert.That(response.InstallationCommand, Does.Contain(
                $"--label automaton.installation-id='{response.InstallationId}'"));
            Assert.That(response.InstallationCommand, Does.Contain(
                "docker volume create --label automaton.managed=true --label automaton.resource=machine-workspace-volume"));
            Assert.That(response.InstallationCommand, Does.Contain(
                "docker run --rm --user 0:0 -v '/opt/automaton/self-hosted-runners/" +
                response.InstallationId + ":/app/config' --entrypoint chown \"$resolved_image\" automaton:automaton /app/config"));
            Assert.That(response.InstallationCommand, Does.Contain("volume:/automaton/storage"));
            Assert.That(response.InstallationCommand, Does.Contain("/var/run/docker.sock:/var/run/docker.sock"));
            Assert.That(response.InstallationCommand, Does.Contain(
                $"runtime_network='automaton-self-hosted-{response.InstallationId}-runtime'"));
            Assert.That(response.InstallationCommand, Does.Contain(
                "docker network inspect --format '{{(index .IPAM.Config 0).Gateway}}' \"$runtime_network\""));
            Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_RUNTIME_HOST_ADDRESS=\"$runtime_host_address\""));
            Assert.That(response.InstallationCommand, Does.Contain("--network \"$runtime_network\""));
            Assert.That(response.InstallationCommand, Does.Not.Contain("--privileged"));
            Assert.That(response.InstallationCommand, Does.Not.Contain("/:/host"));
            Assert.That(response.InstallationCommand, Does.Not.Contain("--network host"));
        });
    }

    [Test]
    public async Task MultiplePrivilegedInstallations_UseDistinctIdentityAndNoHostPorts()
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var service = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());

        var first = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "privileged-one",
            ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost
        }, CancellationToken.None);
        var second = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "privileged-two",
            ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost
        }, CancellationToken.None);

        Assert.That(second.InstallationId, Is.Not.EqualTo(first.InstallationId));
        Assert.That(second.InstallationCommand, Is.Not.EqualTo(first.InstallationCommand));
        Assert.That(first.InstallationCommand, Does.Not.Contain("--network host"));
        Assert.That(second.InstallationCommand, Does.Not.Contain("--network host"));
        Assert.That(first.InstallationCommand, Does.Not.Contain("-p 8001"));
        Assert.That(first.InstallationCommand, Does.Not.Contain("-p 8080"));
        Assert.That(first.InstallationCommand, Does.Not.Contain("--publish"));
        Assert.That(second.InstallationCommand, Does.Not.Contain("-p 8001"));
        Assert.That(second.InstallationCommand, Does.Not.Contain("-p 8080"));
        Assert.That(second.InstallationCommand, Does.Not.Contain("--publish"));
    }

    [Test]
    public async Task Installer_VerifiesStartupAndReportsBoundedFailureWithoutTracingSecrets()
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var service = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());

        var response = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "installer-feedback",
            ApplicationProfile = RunnerApplicationProfiles.Standard
        }, CancellationToken.None);

        Assert.That(response.InstallationCommand, Does.Contain("command -v docker"));
        Assert.That(response.InstallationCommand, Does.Contain("docker info"));
        Assert.That(response.InstallationCommand, Does.Contain("docker pull"));
        Assert.That(response.InstallationCommand, Does.Contain("docker image inspect --format '{{index .RepoDigests 0}}'"));
        Assert.That(response.InstallationCommand, Does.Contain("did not resolve to an immutable repository digest"));
        Assert.That(response.InstallationCommand, Does.Contain("{{.State.Status}}"));
        Assert.That(response.InstallationCommand, Does.Contain("restarting|exited|dead"));
        Assert.That(response.InstallationCommand, Does.Contain("docker logs --tail 80"));
        Assert.That(response.InstallationCommand, Does.Contain("[REDACTED]"));
        Assert.That(response.InstallationCommand, Does.Contain("exit 1"));
        Assert.That(response.InstallationCommand, Does.Contain("Runner container is running."));
        Assert.That(response.InstallationCommand, Does.Contain("Waiting for it to enroll and connect to Automaton."));
        Assert.That(response.InstallationCommand, Does.Not.Contain("set -x"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("docker inspect $container_name"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("echo $AUTOMATON_ENROLLMENT_TOKEN"));
    }

    [Test]
    public async Task Status_DistinguishesPendingEnrolledConnectedExpiredAndOffline()
    {
        await using var scenario = await StatusScenario.CreateAsync();
        var statuses = await scenario.ReadTransitionsAsync();
        Assert.That(statuses, Is.EqualTo(new[]
        {
            SelfHostedRunnerInstallationStatuses.WaitingForEnrollment,
            SelfHostedRunnerInstallationStatuses.RunnerEnrolled,
            SelfHostedRunnerInstallationStatuses.RunnerConnected,
            SelfHostedRunnerInstallationStatuses.OfflineAfterEnrollment,
            SelfHostedRunnerInstallationStatuses.Expired
        }));
    }

    [Test]
    public async Task Status_DoesNotExposeAnotherUsersInstallation()
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var ownerService = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());
        var installation = await ownerService.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "private-status",
            ApplicationProfile = RunnerApplicationProfiles.Standard
        }, CancellationToken.None);

        var otherUserService = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());
        var status = await otherUserService.GetStatusAsync(installation.InstallationId, CancellationToken.None);

        Assert.That(status, Is.Null);
    }

    [TestCase("unknown")]
    [TestCase("Infrastructure")]
    public async Task Create_RejectsNonApplicationProfiles(string profile)
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var service = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());

        Assert.That(
            async () => await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
            {
                RunnerName = "invalid",
                ApplicationProfile = profile
            }, CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task Exchange_AssignsEnrollmentProfileAndNormalCapabilities_AndCannotBeReplayed()
    {
        await using var scenario = await EnrollmentExchangeScenario.CreateAsync();
        var result = await scenario.ExchangeAndReadResultAsync();
        AssertEnrollmentResult(result);
    }

    private static void AssertEnrollmentResult(EnrollmentExchangeResult result)
    {
        Assert.That(result.Purpose, Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(result.Profile, Is.EqualTo(RunnerApplicationProfiles.PrivilegedHost));
        Assert.That(result.ResponseProfile, Is.EqualTo(RunnerApplicationProfiles.PrivilegedHost));
        Assert.That(result.Capabilities, Does.Contain(RunnerCapabilities.ApplicationHosting));
        Assert.That(result.Capabilities, Does.Contain(RunnerCapabilities.WorkflowExecution));
        Assert.That(result.Capabilities, Does.Not.Contain(RunnerCapabilities.DockerManagement));
        Assert.That(result.HostedRunners, Is.Zero);
        Assert.That(result.RunnerHosts, Is.Zero);
        Assert.That(result.UserLinked, Is.True);
        Assert.That(result.ApiKey, Does.StartWith("ak_runner_"));
        Assert.That(result.ReplayWasRejected, Is.True);
    }

    private sealed class StatusScenario : IAsyncDisposable
    {
        private readonly ApplicationDbContext db;
        private readonly SelfHostedRunnerInstallationService service;
        private readonly string installationId;
        private readonly SelfHostedRunnerEnrollment enrollment;

        private StatusScenario(
            ApplicationDbContext db,
            SelfHostedRunnerInstallationService service,
            string installationId,
            SelfHostedRunnerEnrollment enrollment)
        {
            this.db = db;
            this.service = service;
            this.installationId = installationId;
            this.enrollment = enrollment;
        }

        public static async Task<StatusScenario> CreateAsync()
        {
            var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
            var service = SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid());
            var installation = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
            {
                RunnerName = "status-runner",
                ApplicationProfile = RunnerApplicationProfiles.Standard
            }, CancellationToken.None);
            var enrollment = await db.SelfHostedRunnerEnrollments.SingleAsync();
            return new StatusScenario(db, service, installation.InstallationId, enrollment);
        }

        public async Task<string[]> ReadTransitionsAsync()
        {
            var statuses = new List<string>
            {
                (await service.GetStatusAsync(installationId, CancellationToken.None))!.Status
            };
            await CompleteEnrollmentAsync();
            statuses.Add((await service.GetStatusAsync(installationId, CancellationToken.None))!.Status);
            await ConnectRunnerAsync();
            statuses.Add((await service.GetStatusAsync(installationId, CancellationToken.None))!.Status);
            await MakeRunnerOfflineAsync();
            statuses.Add((await service.GetStatusAsync(installationId, CancellationToken.None))!.Status);
            await ExpireEnrollmentAsync();
            statuses.Add((await service.GetStatusAsync(installationId, CancellationToken.None))!.Status);
            return [.. statuses];
        }

        private async Task CompleteEnrollmentAsync()
        {
            var runner = new Automaton.Api.Entities.Runner
            {
                Id = Guid.NewGuid(),
                Name = enrollment.RunnerName,
                Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.Standard,
                InstallationId = installationId,
                Status = RunnerConnectivityStatuses.Unknown
            };
            db.Runners.Add(runner);
            enrollment.Status = RunnerEnrollmentStatuses.Completed;
            enrollment.RunnerId = runner.Id;
            await db.SaveChangesAsync();
        }

        private async Task ConnectRunnerAsync()
        {
            var runner = await db.Runners.SingleAsync();
            runner.ConnectionId = "connection";
            runner.LastConnectedAtUtc = DateTime.UtcNow;
            runner.LastHeartbeatAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        private async Task MakeRunnerOfflineAsync()
        {
            var runner = await db.Runners.SingleAsync();
            runner.ConnectionId = null;
            runner.LastHeartbeatAtUtc = DateTime.UtcNow.Subtract(RunnerPresenceEvaluator.OfflineThreshold).AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        private async Task ExpireEnrollmentAsync()
        {
            enrollment.Status = RunnerEnrollmentStatuses.Pending;
            enrollment.RunnerId = null;
            enrollment.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class EnrollmentExchangeScenario : IAsyncDisposable
    {
        private readonly ApplicationDbContext db;
        private readonly SelfHostedRunnerInstallationService service;
        private readonly Guid userId;
        private readonly string token;

        private EnrollmentExchangeScenario(ApplicationDbContext db, SelfHostedRunnerInstallationService service, Guid userId, string token)
        {
            this.db = db;
            this.service = service;
            this.userId = userId;
            this.token = token;
        }

        public static async Task<EnrollmentExchangeScenario> CreateAsync()
        {
            var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
            var userId = Guid.NewGuid();
            var service = SelfHostedRunnerInstallationTestServices.CreateService(db, userId);
            var installation = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
            {
                RunnerName = "registered-self-hosted",
                ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost
            }, CancellationToken.None);
            var token = Regex.Match(
                installation.InstallationCommand,
                "AUTOMATON_ENROLLMENT_TOKEN='([^']+)'").Groups[1].Value;
            return new EnrollmentExchangeScenario(db, service, userId, token);
        }

        public async Task<EnrollmentExchangeResult> ExchangeAndReadResultAsync()
        {
            var response = await ExchangeAsync();
            var runner = await db.Runners.SingleAsync();
            var userLinked = await db.RunnerUsers.AnyAsync(x => x.RunnerId == runner.Id && x.UserId == userId);
            var result = new EnrollmentExchangeResult(
                runner.Purpose,
                runner.ApplicationProfile,
                runner.CapabilitiesJson,
                await db.HostedRunners.CountAsync(),
                await db.RunnerHosts.CountAsync(),
                userLinked,
                response?.ApiKey,
                response?.ApplicationProfile);
            var replayWasRejected = await ReplayAsync() is null;
            return result with { ReplayWasRejected = replayWasRejected };
        }

        private Task<RunnerEnrollmentExchangeResponse?> ExchangeAsync()
            => service.ExchangeAsync(new RunnerEnrollmentExchangeRequest
            {
                EnrollmentToken = token,
                RunnerName = "registered-self-hosted",
                RunnerVersion = "test",
                MachineName = "user-host"
            }, CancellationToken.None);

        private Task<RunnerEnrollmentExchangeResponse?> ReplayAsync()
            => service.ExchangeAsync(
                new RunnerEnrollmentExchangeRequest { EnrollmentToken = token },
                CancellationToken.None);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed record EnrollmentExchangeResult(
        string Purpose,
        string? Profile,
        string Capabilities,
        int HostedRunners,
        int RunnerHosts,
        bool UserLinked,
        string? ApiKey,
        string? ResponseProfile,
        bool ReplayWasRejected = false);

}
