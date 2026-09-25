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
public class HostedRunnerProfileAuditTests
{
    [Test]
    public async Task RegistrationDoesNotPersistSelfReportedApplicationProfile()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var accessor = HostedRunnerAuditTestHttpContext.Create(userId, "runner-owner@automaton.test");
        var userContext = new UserContextService(accessor);
        var runnerService = HostedRunnerProfileTestServiceFactory.Create(db, userContext);

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
    public void InfrastructureReconnectDoesNotInferCapabilitiesFromProtocol()
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infrastructure-restricted-report",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                RunnerPurposes.Infrastructure)
        };

        RunnerService.ApplyInstallationReport(runner, new RunnerDetails
        {
            Purpose = RunnerPurposes.Infrastructure,
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.HostContainerExecution,
            Capabilities = [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution]
        });

        Assert.That(
            RunnerCapabilities.Deserialize(runner.CapabilitiesJson, runner.Purpose),
            Is.EquivalentTo(new[] { RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution }));
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
}
