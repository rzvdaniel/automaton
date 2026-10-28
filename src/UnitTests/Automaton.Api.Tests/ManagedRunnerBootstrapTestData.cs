using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerBootstrapTestData
{
    public static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    public static void SeedUser(ApplicationDbContext db, Guid userId, Guid? planId)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"user-{userId:N}@automaton.test",
            Email = $"user-{userId:N}@automaton.test",
            PublicUsername = $"user-{userId:N}",
            SubscriptionPlanId = planId,
        });
        db.SaveChanges();
    }

    public static WebApp SeedBackendApp(ApplicationDbContext db, Guid ownerId)
    {
        var app = new WebApp
        {
            Id = Guid.NewGuid(),
            Name = "Backend App",
            Summary = "Summary",
            Description = "Description",
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };

        db.WebApps.Add(app);
        db.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = app.Id,
            UserId = ownerId
        });
        db.SaveChanges();
        return app;
    }

    public static Guid SeedHostedRunnerPlan(ApplicationDbContext db, bool enabled)
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
            Name = enabled ? "Hosted" : "No Hosted",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(PlanCapabilities.Default)
        };

        db.HostedRunnerSpecifications.Add(specification);
        db.SubscriptionPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    public static RunnerHost SeedRunnerHost(ApplicationDbContext db, bool remote = false)
    {
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-a",
            Provider = remote ? RunnerHostProviders.Hetzner : RunnerHostProviders.Local,
            Status = RunnerHostStatuses.Active,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            RuntimeConnectivityMode = remote
                ? RunnerHostRuntimeConnectivityModes.PublicHostPort
                : RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            PublicIp = remote ? "203.0.113.10" : null,
            InfrastructureRunnerId = Guid.NewGuid(),
            TotalCpuCores = 8,
            TotalMemoryMb = 16384,
            TotalDiskMb = 65536,
            CreatedAt = DateTime.UtcNow,
            StorageState = RunnerHostStorageStates.Healthy,
            StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
            StorageAvailableBytes = 60L * 1024 * 1024 * 1024,
            StorageReserveRequiredBytes = 5L * 1024 * 1024 * 1024,
            StorageMeasuredAtUtc = DateTime.UtcNow,
            DockerFilesystemTotalBytes = 65536L * 1024 * 1024
        };

        db.RunnerHosts.Add(host);
        db.SaveChanges();
        return host;
    }

    public static Runner SeedConnectedRunner(
        ApplicationDbContext db,
        string apiCompatibilityVersion = RunnerApiCompatibilityVersions.ContainerApplicationExecution)
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "managed-runner",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = "Standard",
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                RunnerPurposes.Managed),
            ConnectionId = "conn-1",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            LastConnectedAtUtc = DateTime.UtcNow,
            Status = "online",
            ApiCompatibilityVersion = apiCompatibilityVersion
        };

        db.Runners.Add(runner);
        db.SaveChanges();
        return runner;
    }

    public static Runner SeedSelfHostedRunner(ApplicationDbContext db, Guid ownerId, string applicationProfile)
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = $"self-hosted-{applicationProfile}",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = applicationProfile,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                RunnerPurposes.Managed),
            ConnectionId = "self-hosted-connection",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            LastConnectedAtUtc = DateTime.UtcNow,
            Status = "online",
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.ContainerApplicationExecution
        };

        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = ownerId });
        db.SaveChanges();
        return runner;
    }

    public static void SeedRunningHostedRunner(ApplicationDbContext db, Guid ownerId, Guid planId, Runner runner)
    {
        var host = SeedRunnerHost(db);
        db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            RunnerHostId = host.Id,
            RunnerHost = host,
            RunnerId = runner.Id,
            Runner = runner,
            Name = "Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            ContainerName = "automaton-hosted-runner-test",
            RuntimeEndpoint = "http://automaton-hosted-runner-test:8001",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = ownerId });
        db.SaveChanges();
    }

    public static Guid PrepareConnectedHostedRunnerAssignment(
        ApplicationDbContext db,
        Guid ownerId,
        WebApp app,
        bool legacyApi = false,
        bool disabled = false,
        bool deleted = false,
        bool assignToApp = true)
    {
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db);
        var runner = SeedConnectedRunner(db,
            legacyApi ? RunnerApiCompatibilityVersions.Legacy : RunnerApiCompatibilityVersions.ContainerApplicationExecution);
        SeedRunningHostedRunner(db, ownerId, planId, runner);
        var hostedRunner = db.HostedRunners.Single();
        if (disabled)
            hostedRunner.Status = HostedRunnerStatuses.Disabled;
        if (deleted)
            hostedRunner.DeletedAt = DateTime.UtcNow;
        if (assignToApp)
            app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = runner.Id;
        db.SaveChanges();
        return runner.Id;
    }

    public static Guid PrepareSelfHostedRunnerAssignment(
        ApplicationDbContext db,
        Guid ownerId,
        WebApp app,
        string profile,
        bool offline = false)
    {
        SeedUser(db, ownerId, planId: null);
        var runner = SeedSelfHostedRunner(db, ownerId, profile);
        if (offline)
        {
            runner.ConnectionId = null;
            runner.LastHeartbeatAtUtc = DateTime.UtcNow.AddMinutes(-5);
        }
        var target = app.DeploymentTargets.Single(candidate => candidate.IsDefault);
        target.ExecutionRunnerId = runner.Id;
        db.SaveChanges();
        return runner.Id;
    }

    public static void SeedOnlineInfrastructureRunnerAsHostedRunnerRecord(ApplicationDbContext db, Guid ownerId)
    {
        var runner = CreateOnlineInfrastructureRunner();
        db.Runners.Add(runner);
        db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            RunnerId = runner.Id,
            Runner = runner,
            Name = "Legacy Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            ContainerName = "legacy-hosted-runner",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    public static Guid SeedLegacyInfrastructureAssignment(ApplicationDbContext db, Guid ownerId, WebApp app)
    {
        var runner = CreateOnlineInfrastructureRunner();
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = ownerId });
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = runner.Id;
        db.SaveChanges();
        return runner.Id;
    }

    public static void SeedMissingPersistedAssignment(ApplicationDbContext db, Guid ownerId, WebApp app)
    {
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db);
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = Guid.NewGuid();
        db.SaveChanges();
    }

    private static Runner CreateOnlineInfrastructureRunner() => new()
    {
        Id = Guid.NewGuid(),
        Name = "infrastructure-runner",
        Purpose = "Infrastructure",
        CapabilitiesJson = "[\"HostExecution\"]",
        ConnectionId = "infrastructure-connection",
        LastHeartbeatAtUtc = DateTime.UtcNow,
        LastConnectedAtUtc = DateTime.UtcNow,
        Status = "online",
        ApiCompatibilityVersion = RunnerApiCompatibilityVersions.ContainerApplicationExecution
    };

}

