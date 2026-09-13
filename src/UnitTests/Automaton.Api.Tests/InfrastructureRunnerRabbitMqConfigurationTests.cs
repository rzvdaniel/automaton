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
    public void TryBuildEnvironment_UsesRunnerEndpointAndCanonicalCredentials()
    {
        var rabbitMq = new RabbitMqOptions
        {
            Host = "rabbitmq",
            Port = 5672,
            Username = "infra",
            Password = "secret",
            VirtualHost = "/",
            ExchangeName = "cap.default.router"
        };
        var runnerRabbitMq = new InfrastructureRunnerRabbitMqOptions
        {
            Host = "10.0.0.3",
            Port = 5672
        };

        Assert.That(InfrastructureRunnerRabbitMqConfigurationService.TryBuildEnvironment(rabbitMq, runnerRabbitMq, out var environment), Is.True);
        Assert.That(environment, Does.Contain("RabbitMq__Host=10.0.0.3"));
        Assert.That(environment, Does.Contain("RabbitMq__Port=5672"));
        Assert.That(environment, Does.Contain("RabbitMq__Username=infra"));
        Assert.That(environment, Does.Contain("RabbitMq__Password=secret"));
        Assert.That(environment, Does.Contain("RabbitMq__VirtualHost=/"));
        Assert.That(environment, Does.Contain("RabbitMq__ExchangeName=cap.default.router"));
    }

    [TestCase("")]
    [TestCase("line\nbreak")]
    public void TryBuildEnvironment_FailsClosedForInvalidRunnerEndpoint(string host)
    {
        var rabbitMq = new RabbitMqOptions
        {
            Host = "rabbitmq",
            Port = 5672,
            Username = "infra",
            Password = "secret"
        };
        var runnerRabbitMq = new InfrastructureRunnerRabbitMqOptions { Host = host };

        Assert.That(InfrastructureRunnerRabbitMqConfigurationService.TryBuildEnvironment(rabbitMq, runnerRabbitMq, out _), Is.False);
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
                Host = "rabbitmq",
                Port = 5672,
                Username = "infra",
                Password = "secret",
                VirtualHost = "/",
                ExchangeName = "cap.default.router"
            }),
            Options.Create(new InfrastructureRunnerRabbitMqOptions { Host = "10.0.0.3" }),
            enrollmentService);

        var environment = await configurationService.ResolveEnvironmentAsync(
            apiKey: null,
            enrollmentToken: created.EnrollmentToken,
            CancellationToken.None);

        Assert.That(environment, Does.Contain("RabbitMq__Host=10.0.0.3"));
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
                Host = "rabbitmq",
                Port = 5672,
                Username = "infra",
                Password = "secret"
            }),
            Options.Create(new InfrastructureRunnerRabbitMqOptions { Host = "10.0.0.3" }),
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
