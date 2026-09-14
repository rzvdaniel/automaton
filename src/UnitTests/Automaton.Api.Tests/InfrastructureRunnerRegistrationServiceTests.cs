using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InfrastructureRunnerRegistrationServiceTests
{
    [Test]
    public async Task ReregistrationPersistsCapabilitiesAdvertisedByCurrentInstallation()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"infrastructure-registration-{Guid.NewGuid():N}")
            .Options);
        var audit = new NoOpAuditEventService();
        var apiKeys = new ApiKeyService(
            db,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            audit);
        var service = new InfrastructureRunnerRegistrationService(
            db,
            apiKeys,
            audit,
            NullLogger<InfrastructureRunnerRegistrationService>.Instance);
        var userId = Guid.NewGuid();
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "infra-host", Provider = "test" };
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var installationId = "ri_capability-refresh";
        var firstKey = await apiKeys.CreateApiKeyAsync(
            userId,
            "test enrollment",
            ApiKeyScope.InfrastructureRunnerEnroll,
            saveChanges: true);
        var advertised = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure);
        var first = await service.RegisterAsync(firstKey.apiKey, Request(installationId, advertised), CancellationToken.None);

        Assert.That(first, Is.Not.Null);
        var runner = await db.Runners.SingleAsync();
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(
            [RunnerCapabilities.ApplicationHosting],
            RunnerPurposes.Infrastructure);
        await db.SaveChangesAsync();

        var secondKey = await apiKeys.CreateApiKeyAsync(
            userId,
            "test refresh",
            ApiKeyScope.InfrastructureRunnerEnroll,
            saveChanges: true);
        var refreshed = await service.RegisterAsync(secondKey.apiKey, Request(installationId, advertised), CancellationToken.None);

        Assert.That(refreshed!.RunnerId, Is.EqualTo(runner.Id));
        Assert.That((await db.Runners.SingleAsync()).CapabilitiesJson,
            Is.EqualTo(RunnerCapabilities.Serialize(advertised, RunnerPurposes.Infrastructure)));
    }

    private static InfrastructureRunnerRegistrationRequest Request(
        string installationId,
        IReadOnlyList<string> capabilities)
        => new()
        {
            InstallationId = installationId,
            RunnerName = "infra-host",
            RunnerVersion = "test",
            MachineName = "infra-host",
            Capabilities = capabilities
        };

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
