using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InfrastructureCommandSecretResolutionTests
{
    [Test]
    public async Task ValidGrantReturnsExecutableDispositionAndSecrets()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db);

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Executable));
        Assert.That(response.EnvironmentVariables["AUTOMATON_TOKEN"], Is.EqualTo("secret-value"));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Dispatching));
    }

    [Test]
    public async Task TerminalDeploymentReturnsExistingTerminalReasonWithoutMutation()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Failed);
        var deployment = await db.HostedRunnerDeployments.SingleAsync();
        deployment.Error = HostedRunnerDeploymentErrors.AmbiguousDispatch;
        deployment.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, Guid.NewGuid()), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
        Assert.That(response.TerminalReason, Is.EqualTo(HostedRunnerDeploymentErrors.AmbiguousDispatch));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).FinishedAt, Is.Not.Null);
    }

    [TestCase(false, false, HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch)]
    [TestCase(true, false, HostedRunnerDeploymentErrors.AmbiguousDispatch)]
    [TestCase(false, true, HostedRunnerDeploymentErrors.AmbiguousDispatch)]
    public async Task UnusableGrantTerminalizesWithEvidenceAwareReason(
        bool delivered,
        bool consumed,
        string expectedReason)
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching,
            expired: true, delivered: delivered, consumed: consumed);

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);
        await AssertSecretResolutionFailureAsync(db, response, expectedReason);
    }

    [Test]
    public async Task MissingGrantTerminalizesBeforeDispatch()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db);

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, Guid.NewGuid()), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
        Assert.That(response.TerminalReason, Is.EqualTo(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Error,
            Is.EqualTo(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch));
    }

    [Test]
    public async Task RevokedGrantBeforeDeliveryTerminalizesBeforeDispatch()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db);
        var grant = await db.ExecutionSecretGrants.SingleAsync();
        grant.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
        Assert.That(response.TerminalReason, Is.EqualTo(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
    }

    [Test]
    public async Task RepeatedResolutionAfterTerminalizationReturnsSameDispositionAndReason()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching,
            expired: true, delivered: true);
        var request = SecretsRequest(fixture, fixture.GrantId);

        var first = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id, request, CancellationToken.None);
        var second = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id, request, CancellationToken.None);

        Assert.That(second.Disposition, Is.EqualTo(first.Disposition));
        Assert.That(second.TerminalReason, Is.EqualTo(first.TerminalReason));
    }

    [Test]
    public async Task DeliveredSingleDeliveryGrantWithoutReadCountTerminalizesAsAmbiguousDispatch()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching,
            expired: true, delivered: true);
        await MarkDeliveredGrantUnreadAsync(db);

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);
        await AssertSecretResolutionFailureAsync(db, response, HostedRunnerDeploymentErrors.AmbiguousDispatch);
    }

    [Test]
    public async Task TerminalResponseRequiresPersistedTerminalDeployment()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching, expired: true,
            transactionalEventExecutor: new NoOpTransactionalEventExecutor());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResolveSecretsAsync(
            fixture.ApiKeyId, fixture.Deployment.Id, SecretsRequest(fixture, fixture.GrantId), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("could not be confirmed as durably persisted"));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Dispatching));
    }

    private static async Task MarkDeliveredGrantUnreadAsync(ApplicationDbContext db)
    {
        var grant = await db.ExecutionSecretGrants.SingleAsync();
        grant.ReadCount = 0;
        grant.ConsumedAtUtc = null;
        await db.SaveChangesAsync();
    }

    private static async Task AssertSecretResolutionFailureAsync(
        ApplicationDbContext db,
        InfrastructureCommandSecretsResponse response,
        string expectedReason)
    {
        var deployment = await db.HostedRunnerDeployments.Include(x => x.HostedRunner).SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
            Assert.That(response.TerminalReason, Is.EqualTo(expectedReason));
            Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(deployment.Error, Is.EqualTo(expectedReason));
            Assert.That(deployment.FinishedAt, Is.Not.Null);
            Assert.That(deployment.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
        });
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    private static InfrastructureCommandSecretsRequest SecretsRequest(ResolutionFixture fixture, Guid grantId) => new(
        fixture.Deployment.Id, fixture.Host.Id, fixture.Runner.Id, "reprovision-hosted-runner", [grantId]);

    private static Task<ResolutionFixture> CreateResolutionFixture(
        ApplicationDbContext db,
        string status = HostedRunnerDeploymentStatuses.Dispatching,
        bool expired = false,
        bool delivered = false,
        bool consumed = false,
        ITransactionalEventExecutor? transactionalEventExecutor = null) =>
        InfrastructureCommandResolutionTestScenarioFactory.CreateAsync(
            db, status, expired, delivered, consumed, transactionalEventExecutor);

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class NoOpTransactionalEventExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
