using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public class InfrastructureRunnerRabbitMqConfigurationTests
{
    [Test]
    public void TryBuildEnvironment_UsesCanonicalRabbitMqSettings()
    {
        var options = new RabbitMqOptions
        {
            Host = "rabbitmq.internal",
            Port = 5672,
            Username = "infra",
            Password = "secret",
            VirtualHost = "/",
            ExchangeName = "cap.default.router"
        };

        Assert.That(InfrastructureRunnerRabbitMqConfigurationService.TryBuildEnvironment(options, out var environment), Is.True);
        Assert.That(environment, Does.Contain("RabbitMq__Host=rabbitmq.internal"));
        Assert.That(environment, Does.Contain("RabbitMq__Port=5672"));
        Assert.That(environment, Does.Contain("RabbitMq__Username=infra"));
        Assert.That(environment, Does.Contain("RabbitMq__Password=secret"));
        Assert.That(environment, Does.Contain("RabbitMq__VirtualHost=/"));
        Assert.That(environment, Does.Contain("RabbitMq__ExchangeName=cap.default.router"));
    }

    [TestCase("")]
    [TestCase("line\nbreak")]
    public void TryBuildEnvironment_FailsClosedForInvalidBrokerSettings(string host)
    {
        var options = new RabbitMqOptions
        {
            Host = host,
            Port = 5672,
            Username = "infra",
            Password = "secret"
        };

        Assert.That(InfrastructureRunnerRabbitMqConfigurationService.TryBuildEnvironment(options, out _), Is.False);
    }

    [Test]
    public async Task ResolveEnvironmentAsync_AllowsFreshInfrastructureEnrollmentTokenWithoutConsumingIt()
    {
        await using var db = CreateDbContext();
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "token-bootstrap-host",
            Provider = RunnerHostProviders.Hetzner
        };
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var audit = new NoopAuditEventService();
        var enrollmentService = TestRunnerEnrollmentFactory.Create(db, audit);
        var created = await enrollmentService.CreateAsync(host.Id, Guid.NewGuid(), CancellationToken.None);
        var apiKeyService = new ApiKeyService(
            db,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            audit);
        var configurationService = new InfrastructureRunnerRabbitMqConfigurationService(
            apiKeyService,
            db,
            Options.Create(new RabbitMqOptions
            {
                Host = "rabbitmq.internal",
                Port = 5672,
                Username = "infra",
                Password = "secret",
                VirtualHost = "/",
                ExchangeName = "cap.default.router"
            }),
            enrollmentService);

        var environment = await configurationService.ResolveEnvironmentAsync(
            apiKey: null,
            enrollmentToken: created.EnrollmentToken,
            CancellationToken.None);

        Assert.That(environment, Does.Contain("RabbitMq__Host=rabbitmq.internal"));
        Assert.That(await db.RunnerEnrollments.SingleAsync(), Has.Property(nameof(RunnerEnrollment.Status))
            .EqualTo(RunnerEnrollmentStatuses.Pending));
    }

    [Test]
    public async Task ResolveEnvironmentAsync_RejectsMissingAndManagedRunnerCredentials()
    {
        await using var db = CreateDbContext();
        var audit = new NoopAuditEventService();
        var apiKeyService = new ApiKeyService(
            db,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            audit);
        var enrollmentService = TestRunnerEnrollmentFactory.Create(db, audit);
        var configurationService = new InfrastructureRunnerRabbitMqConfigurationService(
            apiKeyService,
            db,
            Options.Create(new RabbitMqOptions
            {
                Host = "rabbitmq.internal",
                Port = 5672,
                Username = "infra",
                Password = "secret"
            }),
            enrollmentService);

        Assert.That(await configurationService.ResolveEnvironmentAsync(null), Is.Null);

        var (rawKey, key) = await apiKeyService.CreateApiKeyAsync(
            Guid.NewGuid(),
            "managed-runner",
            ApiKeyScope.Runner,
            saveChanges: true);
        db.Runners.Add(new Runner
        {
            Id = Guid.NewGuid(),
            Name = "managed-runner",
            Purpose = RunnerPurposes.Managed,
            ApiKeyId = key.Id
        });
        await db.SaveChangesAsync();

        // A Runner-scoped key without Infrastructure purpose cannot cross the
        // broker-configuration boundary.
        Assert.That(await configurationService.ResolveEnvironmentAsync(rawKey), Is.Null);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"rabbitmq-config-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class NoopAuditEventService : IAuditEventService
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
