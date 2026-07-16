using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class HostedRunnerAuditTests
{
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
    public async Task EnsureHostedRunnerForUserPlanAsync_WritesRequestedAndSucceededAuditEvents()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db);
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
                Enabled = true,
                DeploymentMode = HostedRunnerDeploymentModes.SshDocker
            });

        var runner = await service.EnsureHostedRunnerForUserPlanAsync(userId, planId);

        Assert.That(runner, Is.Not.Null);

        var auditEvents = await db.AuditEvents
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.That(auditEvents.Select(x => x.EventType), Is.EqualTo(new[]
        {
            AuditEventTypes.HostedRunnerProvisioningRequested,
            AuditEventTypes.HostedRunnerProvisioningSucceeded
        }));

        Assert.That(auditEvents[0].Outcome, Is.EqualTo(AuditOutcomes.Success));
        Assert.That(auditEvents[1].Outcome, Is.EqualTo(AuditOutcomes.Success));
        Assert.That(auditEvents[1].MetadataJson, Does.Not.Contain("ak_runner_"));
        Assert.That(auditEvents[1].MetadataJson, Does.Contain("container-123"));
    }

    [Test]
    public async Task EnsureHostedRunnerForUserPlanAsync_WritesFailedAuditEventWithSafeSummary()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db);
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
                Enabled = true,
                DeploymentMode = HostedRunnerDeploymentModes.SshDocker
            });

        var runner = await service.EnsureHostedRunnerForUserPlanAsync(userId, planId);

        Assert.That(runner, Is.Not.Null);

        var failedEvent = await db.AuditEvents
            .SingleAsync(x => x.EventType == AuditEventTypes.HostedRunnerProvisioningFailed);

        Assert.That(failedEvent.Outcome, Is.EqualTo(AuditOutcomes.Failed));
        Assert.That(failedEvent.Message, Does.Contain("provisioning failed"));
        Assert.That(failedEvent.MetadataJson, Does.Contain("failureSummary"));
        Assert.That(failedEvent.MetadataJson, Does.Not.Contain("token=abc"));
    }

    [Test]
    public async Task PrepareProvisioningAsync_DoesNotOverallocateHostCapacity()
    {
        using var db = CreateDbContext();
        var planId = SeedHostedRunnerPlan(db);
        var host = SeedRunnerHost(db);
        host.TotalCpuCores = 1;
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
        localHost.HostedRunnerSchedulingEnabled = false;

        var remoteHost = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-remote",
            Provider = RunnerHostProviders.Hetzner,
            PublicIp = "203.0.113.10",
            InfrastructureRunnerId = Guid.NewGuid(),
            Status = RunnerHostStatuses.Active,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
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
    public async Task RestartAsAdminAsync_WritesRequestedAndSucceededAuditEvents()
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
            AuditEventTypes.RunnerRestartRequested,
            AuditEventTypes.RunnerRestartSucceeded
        }));

        Assert.That(auditEvents[^1].Outcome, Is.EqualTo(AuditOutcomes.Success));
    }

    [Test]
    public void RestartAsAdminAsync_WritesFailedAuditEvent()
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

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            managementService.RestartAsAdminAsync(hostedRunner.Id, CancellationToken.None));

        var failedEvent = db.AuditEvents.Single(x => x.EventType == AuditEventTypes.RunnerRestartFailed);
        Assert.That(failedEvent.Outcome, Is.EqualTo(AuditOutcomes.Failed));
        Assert.That(failedEvent.MetadataJson, Does.Contain("failureSummary"));
        Assert.That(failedEvent.MetadataJson, Does.Not.Contain("secret=abc"));
    }

    [Test]
    public async Task ReprovisionAsAdminAsync_WritesRequestedAndSucceededAuditEvents()
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
        Assert.That(eventTypes, Does.Contain(AuditEventTypes.RunnerReprovisionSucceeded));
        Assert.That(eventTypes, Does.Contain(AuditEventTypes.HostedRunnerProvisioningRequested));
        Assert.That(eventTypes, Does.Contain(AuditEventTypes.HostedRunnerProvisioningSucceeded));
    }

    [Test]
    public async Task ReprovisionAsAdminAsync_WritesFailedAuditEventWhenProvisioningCannotStart()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = SeedDisabledHostedRunnerPlan(db);
        var hostedRunner = SeedManagedHostedRunner(db, userId, planId);

        var managementService = CreateManagementService(
            db,
            userId,
            new FakeHostedRunnerRemoteCommandExecutor());

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            managementService.ReprovisionAsAdminAsync(hostedRunner.Id, CancellationToken.None));

        var failedEvent = await db.AuditEvents
            .SingleAsync(x => x.EventType == AuditEventTypes.RunnerReprovisionFailed);

        Assert.That(failedEvent.Outcome, Is.EqualTo(AuditOutcomes.Failed));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static Guid SeedHostedRunnerPlan(ApplicationDbContext db)
    {
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
                    CpuCores = 1,
                    MemoryMb = 1024,
                    DiskMb = 4096,
                    MaxApps = 3,
                    Visibility = HostedRunnerVisibility.Public
                }
            })
        };

        db.SubscriptionPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static Guid SeedDisabledHostedRunnerPlan(ApplicationDbContext db)
    {
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "No Hosted",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability
                {
                    Enabled = false
                }
            })
        };

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
            ProvisioningTransport = RunnerHostProvisioningTransports.LocalDocker,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
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
            SubscriptionPlanId = effectivePlanId,
            RunnerHostId = host.Id,
            RunnerHost = host,
            RunnerId = runner.Id,
            Runner = runner,
            Name = "Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            CpuCores = 1,
            MemoryMb = 1024,
            DiskMb = 4096,
            MaxApps = 3,
            ContainerName = "automaton-hosted-runner-test",
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
            apiKeyService,
            new HostedRunnerRemoteCommandExecutorResolver([remoteExecutor]),
            new TestOptionsSnapshot<HostedRunnerOptions>(options),
            auditEventService,
            NullLogger<HostedRunnerProvisioningService>.Instance);
    }

    private static HostedRunnerManagementService CreateManagementService(
        ApplicationDbContext db,
        Guid actorUserId,
        FakeHostedRunnerRemoteCommandExecutor remoteExecutor)
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
                Enabled = true,
                DeploymentMode = HostedRunnerDeploymentModes.SshDocker
            });

        return new HostedRunnerManagementService(
            db,
            provisioningService,
            new HostedRunnerRemoteCommandExecutorResolver([remoteExecutor]),
            new TestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions
            {
                Enabled = true,
                DeploymentMode = HostedRunnerDeploymentModes.SshDocker
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

    private sealed class FakeHostedRunnerRemoteCommandExecutor : IHostedRunnerRemoteCommandExecutor
    {
        public HostedRunnerRemoteExecutionResult ScriptResult { get; init; } =
            new(true, 0, string.Empty, string.Empty);

        public HostedRunnerRemoteExecutionResult ProvisionResult { get; init; } =
            new(true, 0, "container-default", string.Empty);

        public bool CanHandle(RunnerHost host, HostedRunnerOptions options) => true;

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

    private sealed class TestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;

        public T Get(string? name) => value;
    }
}
