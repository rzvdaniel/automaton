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
        await using var fixture = await RegistrationFixture.CreateAsync();

        var installationId = "ri_capability-refresh";
        var firstKey = await fixture.CreateEnrollmentKeyAsync("test enrollment");
        var advertised = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure);
        var first = await fixture.Service.RegisterAsync(firstKey, Request(installationId, advertised), CancellationToken.None);

        Assert.That(first, Is.Not.Null);
        var runner = await fixture.Db.Runners.SingleAsync();
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(
            [RunnerCapabilities.ApplicationHosting],
            RunnerPurposes.Infrastructure);
        await fixture.Db.SaveChangesAsync();

        var secondKey = await fixture.CreateEnrollmentKeyAsync("test refresh");
        var refreshed = await fixture.Service.RegisterAsync(secondKey, Request(installationId, advertised), CancellationToken.None);

        Assert.That(refreshed!.RunnerId, Is.EqualTo(runner.Id));
        Assert.That((await fixture.Db.Runners.SingleAsync()).CapabilitiesJson,
            Is.EqualTo(RunnerCapabilities.Serialize(advertised, RunnerPurposes.Infrastructure)));
    }

    private sealed class RegistrationFixture : IAsyncDisposable
    {
        private readonly ApiKeyService apiKeys;

        private RegistrationFixture(
            ApplicationDbContext db,
            Guid userId,
            ApiKeyService apiKeys,
            InfrastructureRunnerRegistrationService service)
        {
            Db = db;
            UserId = userId;
            this.apiKeys = apiKeys;
            Service = service;
        }

        public ApplicationDbContext Db { get; }
        public Guid UserId { get; }
        public InfrastructureRunnerRegistrationService Service { get; }

        public static async Task<RegistrationFixture> CreateAsync()
        {
            var db = CreateDbContext();
            var userId = Guid.NewGuid();
            await AddRunnerHostAsync(db);
            var (service, apiKeys) = CreateRegistrationServices(db);
            return new RegistrationFixture(db, userId, apiKeys, service);
        }

        public async Task<string> CreateEnrollmentKeyAsync(string displayName)
        {
            var created = await apiKeys.CreateApiKeyAsync(
                UserId, displayName, ApiKeyScope.InfrastructureRunnerEnroll, saveChanges: true);
            return created.apiKey;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"infrastructure-registration-{Guid.NewGuid():N}")
            .Options);

    private static (InfrastructureRunnerRegistrationService Service, ApiKeyService ApiKeys)
        CreateRegistrationServices(ApplicationDbContext db)
    {
        var audit = new NoOpAuditEventService();
        var apiKeys = new ApiKeyService(db, NullLogger<ApiKeyService>.Instance, null!, audit);
        var service = new InfrastructureRunnerRegistrationService(
            db, apiKeys, audit, NullLogger<InfrastructureRunnerRegistrationService>.Instance);
        return (service, apiKeys);
    }

    private static async Task<RunnerHost> AddRunnerHostAsync(ApplicationDbContext db)
    {
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "infra-host", Provider = "test" };
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();
        return host;
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
