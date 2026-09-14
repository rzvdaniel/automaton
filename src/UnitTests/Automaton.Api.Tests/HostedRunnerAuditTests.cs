using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public partial class HostedRunnerAuditTests
{
    [Test]
    public async Task RegistrationDoesNotPersistSelfReportedApplicationProfile()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var accessor = CreateHttpContextAccessor(userId, "runner-owner@automaton.test");
        var userContext = new UserContextService(accessor);
        var runnerService = new RunnerService(
            db,
            userContext,
            automatonHub: null!,
            authorizationService: null!,
            new ApiInstanceIdentityService(),
            new NoOpAuditEventService(),
            applicationEventService: null!,
            managedRunnerAppAssignmentService: null!);

        await runnerService.Add(new RunnerDetails
        {
            Id = runnerId,
            Name = "profile-self-report",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost
        });

        var persisted = await db.Runners.SingleAsync(x => x.Id == runnerId);
        Assert.That(persisted.ApplicationProfile, Is.Null);
    }

    [Test]
    public void ReconnectAndHeartbeatInstallationReportsCannotChangePersistedProfile()
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "profile-authoritative",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost,
            ImageDigest = "old"
        };

        RunnerService.ApplyInstallationReport(runner, new RunnerDetails
        {
            ApplicationProfile = RunnerApplicationProfiles.Standard,
            ImageDigest = "new"
        });

        Assert.That(runner.ApplicationProfile, Is.EqualTo(RunnerApplicationProfiles.PrivilegedHost));
        Assert.That(runner.ImageDigest, Is.EqualTo("new"));
    }

    [Test]
    public void InfrastructureReconnectRefreshesAdvertisedCapabilities()
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infrastructure-capability-refresh",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution],
                RunnerPurposes.Infrastructure)
        };

        RunnerService.ApplyInstallationReport(runner, new RunnerDetails
        {
            Purpose = RunnerPurposes.Infrastructure,
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.HostContainerExecution,
            Capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure)
        });

        Assert.That(
            RunnerCapabilities.Deserialize(runner.CapabilitiesJson, runner.Purpose),
            Is.EquivalentTo(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure)));
    }

    [Test]
    public void ManagedReconnectCannotGrantPrivilegedCapabilities()
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "managed-capability-boundary",
            Purpose = RunnerPurposes.Managed,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                RunnerPurposes.Managed)
        };

        RunnerService.ApplyInstallationReport(runner, new RunnerDetails
        {
            Purpose = RunnerPurposes.Managed,
            Capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.HostContainerExecution
        });

        Assert.That(
            RunnerCapabilities.Deserialize(runner.CapabilitiesJson, runner.Purpose),
            Is.EquivalentTo(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed)));
    }

    [Test]
    public void SanitizeMetadataJson_RedactsExpandedSensitiveKeys()
    {
        var json = AuditEventService.SanitizeMetadataJson(new
        {
            hash = "abc123",
            connectionString = "Server=db;Password=secret;",
            key = "plain-key",
            nested = new
            {
                token = "token-value",
                password = "password-value",
                ok = "safe"
            }
        });

        Assert.That(json, Does.Not.Contain("abc123"));
        Assert.That(json, Does.Not.Contain("Server=db"));
        Assert.That(json, Does.Not.Contain("plain-key"));
        Assert.That(json, Does.Not.Contain("token-value"));
        Assert.That(json, Does.Not.Contain("password-value"));
        Assert.That(json, Does.Contain("[REDACTED]"));
        Assert.That(json, Does.Contain("safe"));
    }

    [Test]
    public async Task EnsureHostedRunnerForUserPlanAsync_WritesRequestedAuditAndQueuesProvisioning()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db);
        SeedUser(db, userId, planId);
        SeedRunnerHost(db);

        var service = CreateProvisioningService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor
            {
                ProvisionResult = new HostedRunnerRemoteExecutionResult(true, 0, "log output\ncontainer-123", string.Empty)
            },
            new HostedRunnerOptions
            {
                Enabled = true
            });

        var runner = await service.EnsureHostedRunnerAsync(userId);

        Assert.That(runner, Is.Not.Null);

        var auditEvents = await db.AuditEvents
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.That(auditEvents.Select(x => x.EventType), Is.EqualTo(new[]
        {
            AuditEventTypes.HostedRunnerProvisioningRequested,
            AuditEventTypes.RunnerEnrollmentCreated
        }));
        Assert.That(auditEvents[0].Outcome, Is.EqualTo(AuditOutcomes.Success));
        Assert.That(runner!.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        Assert.That(await db.SecretVersions.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.ExecutionSecretGrants.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureHostedRunnerForUserPlanAsync_DoesNotRunDockerSynchronously()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db);
        SeedUser(db, userId, planId);
        SeedRunnerHost(db);

        var service = CreateProvisioningService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor
            {
                ProvisionResult = new HostedRunnerRemoteExecutionResult(false, 17, "stdout with token=abc", "remote secret token=abc")
            },
            new HostedRunnerOptions
            {
                Enabled = true
            });

        var runner = await service.EnsureHostedRunnerAsync(userId);

        Assert.That(runner, Is.Not.Null);

        Assert.That(runner!.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        Assert.That(await db.AuditEvents.AnyAsync(x => x.EventType == AuditEventTypes.HostedRunnerProvisioningFailed), Is.False);
    }

    [Test]
    public async Task PrepareProvisioningAsync_DoesNotOverallocateHostCapacity()
    {
        using var db = CreateDbContext();
        var planId = SeedHostedRunnerPlan(db);
        var host = SeedRunnerHost(db);
        // The Free specification requests 0.50 CPU, so this host can fit one
        // Runner but must reject a second.
        host.TotalCpuCores = 0.75m;
        host.TotalMemoryMb = 1024;
        host.TotalDiskMb = 4096;
        db.SaveChanges();

        var service = CreateProvisioningService(
            db,
            Guid.NewGuid(),
            new FakeHostedRunnerRemoteCommandExecutor(),
            new HostedRunnerOptions { Enabled = false });

        var first = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);
        var second = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);

        Assert.That(first.HostedRunner.RunnerHostId, Is.EqualTo(host.Id));
        Assert.That(first.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Pending));
        Assert.That(second.HostedRunner.RunnerHostId, Is.Null);
        Assert.That(second.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
        Assert.That(await db.HostedRunners.CountAsync(x => x.RunnerHostId == host.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task PrepareProvisioningAsync_DoesNotSelectLocalHostWhenSchedulingIsDisabled()
    {
        using var db = CreateDbContext();
        var planId = SeedHostedRunnerPlan(db);
        var localHost = SeedRunnerHost(db);
        localHost.ProvisioningTransport = RunnerHostProvisioningTransports.LocalDocker;
        localHost.InfrastructureRunnerId = null;
        localHost.HostedRunnerSchedulingEnabled = false;

        var remoteHost = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-remote",
            Provider = RunnerHostProviders.Hetzner,
            PublicIp = "203.0.113.10",
            InfrastructureRunnerId = Guid.NewGuid(),
            Status = RunnerHostStatuses.Active,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            HostedRunnerSchedulingEnabled = true,
            TotalCpuCores = 8,
            TotalMemoryMb = 16384,
            TotalDiskMb = 65536,
            CreatedAt = DateTime.UtcNow.AddMinutes(1)
        };
        db.RunnerHosts.Add(remoteHost);
        db.SaveChanges();

        var service = CreateProvisioningService(
            db,
            Guid.NewGuid(),
            new FakeHostedRunnerRemoteCommandExecutor(),
            new HostedRunnerOptions { Enabled = false });

        var prepared = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);

        Assert.That(prepared.HostedRunner.RunnerHostId, Is.EqualTo(remoteHost.Id));
        Assert.That(prepared.HostedRunner.RunnerHostId, Is.Not.EqualTo(localHost.Id));
    }

    [Test]
    public async Task PrepareProvisioningAsync_DoesNotSelectLocalDockerHostWithoutInfrastructureRunner()
    {
        using var db = CreateDbContext();
        var planId = SeedHostedRunnerPlan(db);
        var localHost = SeedRunnerHost(db);
        localHost.ProvisioningTransport = RunnerHostProvisioningTransports.LocalDocker;
        localHost.InfrastructureRunnerId = null;
        localHost.HostedRunnerSchedulingEnabled = true;
        db.SaveChanges();

        var service = CreateProvisioningService(
            db,
            Guid.NewGuid(),
            new FakeHostedRunnerRemoteCommandExecutor(),
            new HostedRunnerOptions { Enabled = false });

        var prepared = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);

        Assert.That(prepared.HostedRunner.RunnerHostId, Is.Null);
        Assert.That(prepared.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
    }

    [Test]
    public void AppModuleExecution_HasUniqueHostedRunnerOperationIndex()
    {
        using var db = CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(AppModuleExecution));
        var index = entity!.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AppModuleExecution.HostedRunnerOperationId) }));

        Assert.That(index.IsUnique, Is.True);
    }

    [Test]
    public async Task RunnerRegistration_CorrelatesByHostedRunnerIdAndCompletesReprovision()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db);
        SeedUser(db, userId, planId);
        var runnerId = Guid.NewGuid();
        var pendingResources = new HostedRunnerResources
        {
            CpuLimit = 2,
            MemoryLimitMb = 2048,
            MemoryReservationMb = 1024,
            PidsLimit = 256,
            DiskLimitMb = 8192
        };
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RunnerId = runnerId,
            Name = "old-display-name",
            ContainerName = "automaton-hosted-runner-registration-test",
            Status = HostedRunnerStatuses.Reprovisioning,
            SpecificationSnapshot = new HostedRunnerSpecificationSnapshot
            {
                SourceSpecificationRevision = 1,
                Resources = new HostedRunnerResources { CpuLimit = 1, MemoryLimitMb = 1024, DiskLimitMb = 4096 }
            },
            PendingSpecificationSnapshot = new HostedRunnerSpecificationSnapshot
            {
                SourceSpecificationRevision = 2,
                Resources = pendingResources
            },
            CreatedAt = DateTime.UtcNow
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Reprovision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
            StartedAt = DateTime.UtcNow
        };
        hostedRunner.Deployments.Add(deployment);
        db.HostedRunners.Add(hostedRunner);
        db.SaveChanges();

        var httpContextAccessor = CreateHttpContextAccessor(userId, "runner-owner@automaton.test");
        var userContext = new UserContextService(httpContextAccessor);
        var audit = new AuditEventService(db, userContext, httpContextAccessor);
        var runnerService = new RunnerService(
            db,
            userContext,
            automatonHub: null!,
            authorizationService: null!,
            new ApiInstanceIdentityService(),
            audit,
            applicationEventService: null!,
            managedRunnerAppAssignmentService: null!);

        await runnerService.Add(new RunnerDetails
        {
            Id = runnerId,
            Name = "new-display-name",
            Purpose = Automaton.Core.Enums.RunnerPurposes.Managed,
            HostedRunnerId = hostedRunner.Id
        });

        Assert.That(hostedRunner.RunnerId, Is.EqualTo(runnerId));
        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Running));
        Assert.That(hostedRunner.PendingSpecificationSnapshot, Is.Null);
        Assert.That(hostedRunner.SpecificationSnapshot.SourceSpecificationRevision, Is.EqualTo(2));
        Assert.That(hostedRunner.SpecificationSnapshot.Resources.MemoryLimitMb, Is.EqualTo(2048));
        Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Succeeded));
        Assert.That(deployment.FinishedAt, Is.Not.Null);
        Assert.That(await db.AuditEvents.AnyAsync(x => x.EventType == AuditEventTypes.RunnerReprovisionSucceeded), Is.True);
    }

    [Test]
    public async Task RestartCompletesOnlyAfterHeartbeatOnNewConnectionSession()
    {
        using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var runner = new Runner
        {
            Id = Guid.NewGuid(), Name = "managed-restart", ConnectionId = "new-connection",
            Status = RunnerConnectivityStatuses.Online, LastHeartbeatAtUtc = now
        };
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = userId, RunnerId = runner.Id,
            Name = "managed-restart", ContainerName = "managed-restart",
            Status = HostedRunnerStatuses.Running
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Restart,
            Status = HostedRunnerDeploymentStatuses.WaitingForReconnect,
            ExpectedRunnerId = runner.Id,
            PreviousConnectionId = "old-connection",
            ReconnectAfterUtc = now.AddSeconds(-5),
            ReconnectDeadlineUtc = now.AddMinutes(2)
        };
        hostedRunner.Deployments.Add(operation);
        var session = new RunnerConnectionSession
        {
            Id = Guid.NewGuid(), RunnerId = runner.Id, ConnectionId = runner.ConnectionId,
            ConnectedAtUtc = now.AddSeconds(-10)
        };
        db.AddRange(runner, hostedRunner, session);
        db.SaveChanges();

        var httpContextAccessor = CreateHttpContextAccessor(userId, "runner-owner@automaton.test");
        var userContext = new UserContextService(httpContextAccessor);
        var runnerService = new RunnerService(
            db, userContext, automatonHub: null!, authorizationService: null!,
            new ApiInstanceIdentityService(), new AuditEventService(db, userContext, httpContextAccessor),
            applicationEventService: null!, managedRunnerAppAssignmentService: null!);

        await runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None);
        Assert.That(operation.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForReconnect));

        session.ConnectedAtUtc = now.AddSeconds(1);
        await db.SaveChangesAsync();
        await runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None);

        Assert.That(operation.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Succeeded));
        Assert.That(operation.FinishedAt, Is.Not.Null);
        Assert.That(await db.AuditEvents.CountAsync(x => x.EventType == AuditEventTypes.RunnerRestartSucceeded), Is.EqualTo(1));
    }

    [Test]
    public async Task RestartCommandSuccessWaitsForReconnectInsteadOfCompletingOperation()
    {
        using var db = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerId = runnerId,
            Name = "restart-command", ContainerName = "restart-command", Status = HostedRunnerStatuses.Running
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Restart,
            Status = HostedRunnerDeploymentStatuses.Executing,
            ExpectedRunnerId = runnerId
        };
        var handler = new InfrastructureActionProvisioningHandler(
            db, new NoOpAuditEventService(),
            Options.Create(new HostedRunnerOptions { RestartReconnectTimeoutSeconds = 60 }),
            Options.Create(new StorageTelemetryOptions()));
        using var result = JsonDocument.Parse("{}");

        await handler.ApplySuccessfulOperationAsync(
            hostedRunner, operation, "restart-hosted-runner", result.RootElement, CancellationToken.None);

        Assert.That(operation.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForReconnect));
        Assert.That(operation.FinishedAt, Is.Null);
        Assert.That(operation.ReconnectAfterUtc, Is.Not.Null);
        Assert.That(operation.ReconnectDeadlineUtc, Is.GreaterThan(operation.ReconnectAfterUtc));
        Assert.That(operation.ReconnectDeadlineUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(61)));
    }

    [Test]
    public async Task SuccessfulHostedRunnerDeleteReleasesRuntimePort()
    {
        using var db = CreateDbContext();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "delete-port",
            ContainerName = "delete-port", RuntimePort = 20000, Status = HostedRunnerStatuses.Deleting
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Deprovision,
            Status = HostedRunnerDeploymentStatuses.Executing
        };
        var handler = new InfrastructureActionProvisioningHandler(
            db, new NoOpAuditEventService(), Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()));
        using var document = JsonDocument.Parse("{\"success\":true}");

        await handler.ApplySuccessfulOperationAsync(
            hostedRunner, deployment, "delete-hosted-runner", document.RootElement, CancellationToken.None);

        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Deleted));
        Assert.That(hostedRunner.RuntimePort, Is.Null);
    }

    [TestCase("{\"success\":true,\"containerName\":\"replacement\"}")]
    [TestCase("{\"success\":true,\"containerId\":\"replacement-id\",\"containerName\":\"\"}")]
    public void HostedRunnerProvisioningRejectsMissingContainerIdentity(string json)
    {
        using var db = CreateDbContext();
        var hostedRunner = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = HostedRunnerStatuses.Provisioning };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id, Status = HostedRunnerDeploymentStatuses.Executing };
        var handler = new InfrastructureActionProvisioningHandler(
            db, new NoOpAuditEventService(), Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()));
        using var document = JsonDocument.Parse(json);

        Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplySuccessfulOperationAsync(
            hostedRunner, deployment, "reprovision-hosted-runner", document.RootElement, CancellationToken.None));
        Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Executing));
        Assert.That(hostedRunner.ContainerId, Is.Null);
    }

    [Test]
    public async Task HostedRunnerProvisioningWithContainerIdentityWaitsForRegistration()
    {
        using var db = CreateDbContext();
        var hostedRunner = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = HostedRunnerStatuses.Provisioning };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id, Status = HostedRunnerDeploymentStatuses.Executing };
        var handler = new InfrastructureActionProvisioningHandler(
            db, new NoOpAuditEventService(), Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()));
        using var document = JsonDocument.Parse("{\"success\":true,\"containerId\":\"replacement-id\",\"containerName\":\"replacement\"}");

        await handler.ApplySuccessfulOperationAsync(
            hostedRunner, deployment, "reprovision-hosted-runner", document.RootElement, CancellationToken.None);

        Assert.That(hostedRunner.ContainerId, Is.EqualTo("replacement-id"));
        Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForRegistration));
    }

    [Test]
    public async Task RestartReconnectTimeoutFailsOnceWithDistinctReason()
    {
        using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "timed-out-restart",
            ContainerName = "timed-out-restart", Status = HostedRunnerStatuses.Running
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Restart,
            Status = HostedRunnerDeploymentStatuses.WaitingForReconnect,
            ReconnectAfterUtc = now.AddMinutes(-3), ReconnectDeadlineUtc = now.AddMinutes(-1)
        };
        hostedRunner.Deployments.Add(operation);
        db.HostedRunners.Add(hostedRunner);
        db.SaveChanges();
        var reconciler = new HostedRunnerRestartReconciler(
            db, new NoOpAuditEventService(), NullLogger<HostedRunnerRestartReconciler>.Instance);

        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.EqualTo(1));
        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.Zero);
        Assert.That(operation.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
        Assert.That(operation.Error, Is.EqualTo(HostedRunnerDeploymentErrors.RestartReconnectTimedOut));
        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
    }

    [Test]
    public async Task RestartConfirmationRollsBackWhenHostedRunnerUpdateFailsThenRetries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var interceptor = new FailHostedRunnerUpdateOnceInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE HostedRunners (
                Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, Name TEXT NOT NULL,
                Status TEXT NOT NULL, LastError TEXT NULL, UpdatedAt TEXT NULL
            );
            CREATE TABLE HostedRunnerDeployments (
                Id TEXT NOT NULL PRIMARY KEY, HostedRunnerId TEXT NOT NULL, Action TEXT NOT NULL,
                Status TEXT NOT NULL, ExpectedRunnerId TEXT NULL, PreviousConnectionId TEXT NULL,
                ReconnectAfterUtc TEXT NULL, ReconnectDeadlineUtc TEXT NULL, StartedAt TEXT NOT NULL,
                FinishedAt TEXT NULL, Error TEXT NULL, Logs TEXT NULL
            );
            CREATE TABLE RunnerConnectionSessions (
                Id TEXT NOT NULL PRIMARY KEY, RunnerId TEXT NOT NULL, ConnectionId TEXT NOT NULL,
                ConnectedAtUtc TEXT NOT NULL
            );
            """);
        var now = DateTime.UtcNow;
        var hostedRunnerId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), Name = "restart", ConnectionId = "new", Status = "online" };
        var operationId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO HostedRunners (Id, UserId, Name, Status) VALUES ({hostedRunnerId}, {Guid.NewGuid()}, {"restart"}, {HostedRunnerStatuses.Failed});
            INSERT INTO HostedRunnerDeployments
                (Id, HostedRunnerId, Action, Status, ExpectedRunnerId, PreviousConnectionId, ReconnectAfterUtc, ReconnectDeadlineUtc, StartedAt)
            VALUES ({operationId}, {hostedRunnerId}, {HostedRunnerDeploymentActions.Restart}, {HostedRunnerDeploymentStatuses.WaitingForReconnect},
                {runner.Id}, {"old"}, {now.AddMinutes(-1)}, {now.AddMinutes(1)}, {now.AddMinutes(-2)});
            INSERT INTO RunnerConnectionSessions (Id, RunnerId, ConnectionId, ConnectedAtUtc)
            VALUES ({Guid.NewGuid()}, {runner.Id}, {runner.ConnectionId}, {now});
            """);
        var runnerService = new RunnerService(
            db, userContextService: null!, automatonHub: null!, authorizationService: null!,
            new ApiInstanceIdentityService(), new NoOpAuditEventService(),
            applicationEventService: null!, managedRunnerAppAssignmentService: null!);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.That(await db.HostedRunnerDeployments.Select(x => x.Status).SingleAsync(),
            Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForReconnect));
        Assert.That(await db.HostedRunners.Select(x => x.Status).SingleAsync(), Is.EqualTo(HostedRunnerStatuses.Failed));

        await runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.That(await db.HostedRunnerDeployments.Select(x => x.Status).SingleAsync(),
            Is.EqualTo(HostedRunnerDeploymentStatuses.Succeeded));
        Assert.That(await db.HostedRunners.Select(x => x.Status).SingleAsync(), Is.EqualTo(HostedRunnerStatuses.Running));
    }

    [Test]
    public async Task PrepareProvisioningAsync_DoesNotMoveRunningRunnerWhenPlanSpecificationChanges()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var specificationA = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = "test-a",
            Name = "Test A",
            IsActive = true,
            Resources = new HostedRunnerResources { CpuLimit = 0.25m, MemoryLimitMb = 768, DiskLimitMb = 2048 }
        };
        var specificationB = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = "test-b",
            Name = "Test B",
            IsActive = true,
            Resources = new HostedRunnerResources { CpuLimit = 2m, MemoryLimitMb = 4096, DiskLimitMb = 8192 }
        };
        var planA = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan A",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability { Enabled = true, SpecificationId = specificationA.Id }
            })
        };
        var planB = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan B",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability { Enabled = true, SpecificationId = specificationB.Id }
            })
        };
        db.HostedRunnerSpecifications.AddRange(specificationA, specificationB);
        db.SubscriptionPlans.AddRange(planA, planB);
        SeedUser(db, userId, planA.Id);

        var hostA = SeedRunnerHost(db);
        hostA.ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner;
        hostA.RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort;
        hostA.PublicIp = "203.0.113.1";
        hostA.InfrastructureRunnerId = Guid.NewGuid();
        hostA.TotalCpuCores = 1;
        hostA.TotalMemoryMb = 1024;
        hostA.TotalDiskMb = 4096;
        var hostB = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-b",
            Provider = RunnerHostProviders.Hetzner,
            PublicIp = "203.0.113.2",
            InfrastructureRunnerId = Guid.NewGuid(),
            Status = RunnerHostStatuses.Active,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            TotalCpuCores = 8,
            TotalMemoryMb = 16384,
            TotalDiskMb = 65536,
            CreatedAt = DateTime.UtcNow.AddMinutes(1)
        };
        db.RunnerHosts.Add(hostB);
        db.SaveChanges();

        var hostedRunner = SeedManagedHostedRunner(db, userId, planA.Id);
        hostedRunner.RunnerHostId = hostA.Id;
        hostedRunner.RunnerHost = hostA;
        hostedRunner.RuntimePort = 20000;
        hostedRunner.RuntimeEndpoint = "http://203.0.113.1:20000";
        hostedRunner.SpecificationSnapshot = HostedRunnerSpecificationSnapshotFactory.Create(
            specificationA,
            DateTime.UtcNow.AddMinutes(-5));
        hostedRunner.PendingSpecificationSnapshot = null;
        db.Users.Single(x => x.Id == userId).SubscriptionPlanId = planB.Id;
        db.SaveChanges();

        var service = CreateProvisioningService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor(),
            new HostedRunnerOptions { Enabled = true, RemoteRuntimePortStart = 20001 });

        var prepared = await service.PrepareProvisioningAsync(userId, Guid.Empty);

        Assert.That(prepared.ShouldPublishProvisionRequest, Is.False);
        Assert.That(prepared.HostedRunner.RunnerHostId, Is.EqualTo(hostA.Id));
        Assert.That(prepared.HostedRunner.RuntimePort, Is.EqualTo(20000));
        Assert.That(prepared.HostedRunner.RuntimeEndpoint, Is.EqualTo("http://203.0.113.1:20000"));
        Assert.That(prepared.HostedRunner.SpecificationSnapshot.SourceSpecificationId, Is.EqualTo(specificationA.Id));
        Assert.That(prepared.HostedRunner.PendingSpecificationSnapshot, Is.Null);
        Assert.That(await db.AuditEvents.CountAsync(x => x.EventType == AuditEventTypes.HostedRunnerProvisioningRequested), Is.EqualTo(0));
    }

    [Test]
    public async Task RestartAsAdminAsync_WritesRequestedAuditAndQueuesAction()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = SeedManagedHostedRunner(db, userId);

        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor
            {
                ScriptResult = new HostedRunnerRemoteExecutionResult(true, 0, "restart ok", string.Empty)
            });

        await managementService.RestartAsAdminAsync(hostedRunner.Id, CancellationToken.None);

        var auditEvents = await db.AuditEvents
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.That(auditEvents.Select(x => x.EventType), Is.EqualTo(new[]
        {
            AuditEventTypes.RunnerRestartRequested
        }));
        Assert.That(db.HostedRunnerDeployments.Single().Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
    }

    [Test]
    public async Task RestartAsAdminAsync_QueuesRestartWithoutVolumeDisposition()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = SeedManagedHostedRunner(db, userId);
        var publisher = new RecordingEventPublisher();
        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor(),
            publisher);

        await managementService.RestartAsAdminAsync(hostedRunner.Id, CancellationToken.None);

        var request = publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();
        Assert.That(request.ActionKey, Is.EqualTo("restart-hosted-runner"));
        Assert.That(request.CommandId, Is.EqualTo(request.OperationId));
        Assert.That(request.RunnerHostId, Is.EqualTo(hostedRunner.RunnerHostId));
        Assert.That(request.InfrastructureRunnerId, Is.EqualTo(hostedRunner.RunnerHost!.InfrastructureRunnerId));
        Assert.That(request.InputJson, Does.Not.Contain("preserveVolumes"));
        Assert.That(request.InputJson, Does.Not.Contain("VolumeName"));
    }

    [TestCase(HostedRunnerDataDispositions.Preserve, true)]
    [TestCase(HostedRunnerDataDispositions.Delete, false)]
    public async Task DeleteAsAdminAsync_PublishesExplicitDataDisposition(
        string disposition,
        bool expectedPreserveVolumes)
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = SeedManagedHostedRunner(db, userId);
        hostedRunner.RuntimePort = 20000;
        await db.SaveChangesAsync();
        var publisher = new RecordingEventPublisher();
        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor(),
            publisher);

        await managementService.DeleteAsAdminAsync(
            hostedRunner.Id,
            disposition,
            CancellationToken.None);

        var request = publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();
        using var input = JsonDocument.Parse(request.InputJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.ActionKey, Is.EqualTo("delete-hosted-runner"));
            Assert.That(input.RootElement.GetProperty("preserveVolumes").GetBoolean(),
                Is.EqualTo(expectedPreserveVolumes));
            Assert.That(input.RootElement.GetProperty("systemVolumeName").GetString(),
                Is.EqualTo($"automaton-hosted-runner-{hostedRunner.Id:N}-system"));
            Assert.That(input.RootElement.GetProperty("userVolumeName").GetString(),
                Is.EqualTo($"automaton-hosted-runner-{hostedRunner.Id:N}-user"));
            Assert.That(hostedRunner.RuntimePort, Is.EqualTo(20000));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("invalid")]
    public void DeleteAsAdminAsync_RejectsMissingOrInvalidDataDisposition(string? disposition)
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor());

        var exception = Assert.ThrowsAsync<ArgumentException>(() =>
            managementService.DeleteAsAdminAsync(
                Guid.NewGuid(),
                disposition!,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("preserve").And.Contain("delete"));
    }

    [Test]
    public async Task RestartAsAdminAsync_DoesNotExecuteDockerSynchronously()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = SeedManagedHostedRunner(db, userId);

        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor
            {
                ScriptResult = new HostedRunnerRemoteExecutionResult(false, 125, string.Empty, "docker restart secret=abc failed")
            });

        await managementService.RestartAsAdminAsync(hostedRunner.Id, CancellationToken.None);

        Assert.That(db.AuditEvents.Any(x => x.EventType == AuditEventTypes.RunnerRestartFailed), Is.False);
        Assert.That(db.HostedRunnerDeployments.Single().Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
    }

    [Test]
    public async Task ReprovisionAsAdminAsync_WritesRequestedAuditAndWaitsForRegistration()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db);
        SeedRunnerHost(db);
        var hostedRunner = SeedManagedHostedRunner(db, userId, planId);

        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor
            {
                ProvisionResult = new HostedRunnerRemoteExecutionResult(true, 0, "provision ok\ncontainer-456", string.Empty)
            });

        await managementService.ReprovisionAsAdminAsync(hostedRunner.Id, CancellationToken.None);

        var eventTypes = await db.AuditEvents
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.EventType)
            .ToListAsync();

        Assert.That(eventTypes, Does.Contain(AuditEventTypes.RunnerReprovisionRequested));
        Assert.That(eventTypes, Does.Not.Contain(AuditEventTypes.RunnerReprovisionSucceeded));
        Assert.That(eventTypes, Does.Contain(AuditEventTypes.HostedRunnerProvisioningRequested));
        Assert.That(eventTypes, Does.Not.Contain(AuditEventTypes.HostedRunnerProvisioningSucceeded));
    }

    [Test]
    public async Task ReprovisionAsAdminAsync_RejectsDisabledPlan()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedDisabledHostedRunnerPlan(db);
        var hostedRunner = SeedManagedHostedRunner(db, userId, planId);

        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            managementService.ReprovisionAsAdminAsync(hostedRunner.Id, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("does not include a hosted runner"));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AppModuleExecution CreateInfrastructureExecution(Guid operationId) => new()
    {
        Id = Guid.NewGuid(),
        HostedRunnerOperationId = operationId,
        AppId = InfrastructureAppTrust.PlatformAppId,
        DeploymentTargetId = Guid.NewGuid(),
        ModuleType = AppModuleTypes.Action,
        ModuleKey = "restart-hosted-runner",
        TriggerType = AppModuleTriggerTypes.Internal,
        Status = AppModuleExecutionStatuses.Running,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static void SeedUser(ApplicationDbContext db, Guid userId, Guid planId)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = $"user-{userId:N}@automaton.test",
            UserName = $"user-{userId:N}@automaton.test",
            PublicUsername = $"user-{userId:N}"[..32],
            SubscriptionPlanId = planId,
            SecurityStamp = Guid.NewGuid().ToString("N")
        });
        db.SaveChanges();
    }

    private static Guid SeedHostedRunnerPlan(ApplicationDbContext db)
    {
        var specification = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = "test-hosted",
            Name = "Test Hosted Runner",
            IsActive = true
        };
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Hosted",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability
                {
                    Enabled = true,
                    Visibility = HostedRunnerVisibility.Public,
                    SpecificationId = specification.Id
                }
            })
        };

        db.HostedRunnerSpecifications.Add(specification);
        db.SubscriptionPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static Guid SeedDisabledHostedRunnerPlan(ApplicationDbContext db)
    {
        var specification = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = "test-disabled",
            Name = "Test Disabled Runner",
            IsActive = true
        };
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "No Hosted",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability
                {
                    Enabled = false,
                    SpecificationId = specification.Id
                }
            })
        };

        db.HostedRunnerSpecifications.Add(specification);
        db.SubscriptionPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static RunnerHost SeedRunnerHost(ApplicationDbContext db)
    {
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-a",
            Provider = RunnerHostProviders.Local,
            Status = RunnerHostStatuses.Active,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            InfrastructureRunnerId = Guid.NewGuid(),
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            TotalCpuCores = 8,
            TotalMemoryMb = 16384,
            TotalDiskMb = 65536,
            CreatedAt = DateTime.UtcNow
        };

        db.RunnerHosts.Add(host);
        db.SaveChanges();
        return host;
    }

    private static HostedRunner SeedManagedHostedRunner(ApplicationDbContext db, Guid userId, Guid? planId = null)
    {
        var host = db.RunnerHosts.FirstOrDefault() ?? SeedRunnerHost(db);
        var effectivePlanId = planId ?? SeedHostedRunnerPlan(db);
        var specificationId = PlanCapabilitiesSerializer.Deserialize(
            db.SubscriptionPlans.Single(x => x.Id == effectivePlanId).CapabilitiesJson).HostedRunner.SpecificationId!.Value;
        if (!db.Users.Any(x => x.Id == userId))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                Email = $"user-{userId:N}@automaton.test",
                UserName = $"user-{userId:N}@automaton.test",
                PublicUsername = $"user-{userId:N}"[..32],
                SubscriptionPlanId = effectivePlanId,
                SecurityStamp = Guid.NewGuid().ToString("N")
            });
        }
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "runner-a",
            Status = "online",
            LastConnectedAtUtc = DateTime.UtcNow
        };
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RunnerHostId = host.Id,
            RunnerHost = host,
            RunnerId = runner.Id,
            Runner = runner,
            Name = "Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            ContainerName = "automaton-hosted-runner-test",
            HostedRunnerSpecificationId = specificationId,
            SpecificationRevision = 1,
            CreatedAt = DateTime.UtcNow
        };

        db.Runners.Add(runner);
        db.HostedRunners.Add(hostedRunner);
        db.SaveChanges();
        return hostedRunner;
    }

    private static HostedRunnerProvisioningService CreateProvisioningService(
        ApplicationDbContext db,
        Guid actorUserId,
        FakeHostedRunnerRemoteCommandExecutor remoteExecutor,
        HostedRunnerOptions options)
    {
        var httpContextAccessor = CreateHttpContextAccessor(actorUserId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(db, userContextService, httpContextAccessor);
        var apiKeyService = new ApiKeyService(
            db,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            auditEventService);

        return new HostedRunnerProvisioningService(
            db,
            TestRunnerEnrollmentFactory.Create(db, auditEventService, options),
            new NoOpEventPublisher(),
            new TestOptionsSnapshot<HostedRunnerOptions>(options),
            new HostedRunnerSpecificationResolver(db),
            auditEventService,
            NullLogger<HostedRunnerProvisioningService>.Instance);
    }

    private static HostedRunnerManagementService CreateManagementService(
        ApplicationDbContext db,
        Guid actorUserId,
        FakeHostedRunnerRemoteCommandExecutor remoteExecutor,
        IEventPublisher? eventPublisher = null)
    {
        var httpContextAccessor = CreateHttpContextAccessor(actorUserId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(db, userContextService, httpContextAccessor);
        var provisioningService = CreateProvisioningService(
            db,
            actorUserId,
            remoteExecutor,
            new HostedRunnerOptions
            {
                Enabled = true
            });

        return new HostedRunnerManagementService(
            db,
            provisioningService,
            eventPublisher ?? new NoOpEventPublisher(),
            new TestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions
            {
                Enabled = true
            }),
            auditEventService,
            userContextService,
            NullLogger<HostedRunnerManagementService>.Instance);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId, string userName)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName)
        ], "Test"));

        return new HttpContextAccessor
        {
            HttpContext = httpContext
        };
    }

    private sealed class FakeHostedRunnerRemoteCommandExecutor
    {
        public HostedRunnerRemoteExecutionResult ScriptResult { get; init; } =
            new(true, 0, string.Empty, string.Empty);

        public HostedRunnerRemoteExecutionResult ProvisionResult { get; init; } =
            new(true, 0, "container-default", string.Empty);

        public Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
            RunnerHost host,
            string script,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ScriptResult);
        }

        public Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
            RunnerHost host,
            string script,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProvisionResult);
        }
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> Messages { get; } = [];

        public Task PublishAsync<T>(
            T message,
            CancellationToken cancellationToken = default) where T : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;

        public T Get(string? name) => value;
    }

    private sealed class FailExecutionInsertOnceInterceptor : SaveChangesInterceptor
    {
        private bool failNext = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (failNext && eventData.Context!.ChangeTracker.Entries<AppModuleExecution>()
                    .Any(x => x.State == EntityState.Added))
            {
                failNext = false;
                throw new InvalidOperationException("Simulated execution insert failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailHostedRunnerUpdateOnceInterceptor : DbCommandInterceptor
    {
        private bool failNext = true;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (failNext && command.CommandText.Contains("UPDATE \"HostedRunners\"", StringComparison.Ordinal))
            {
                failNext = false;
                throw new InvalidOperationException("Simulated hosted Runner state update failure.");
            }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
