using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeEnrollmentMySqlMigrationTests
{
    [Test]
    public async Task Applies_the_real_enrollment_migration_to_an_isolated_mysql_schema()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await using var db = fixture.CreateContext();
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(appliedMigrations, Does.Contain(NodeEnrollmentMySqlTestFixture.EnrollmentMigrationId));
            Assert.That(appliedMigrations, Does.Contain(NodeEnrollmentMySqlTestFixture.ActivationMigrationId));
            Assert.That(db.NodeEnrollmentAuthorizations.Count(), Is.Zero);
            Assert.That(db.NodeEnrollmentRecoveryChallenges.Count(), Is.Zero);
            Assert.That(db.NodeManagement.Count(), Is.Zero);
            Assert.That(db.NodeActivationAttempts.Count(), Is.Zero);
            Assert.That(db.NodeHostFenceReceiptUses.Count(), Is.Zero);
        });
    }
}

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeEnrollmentMySqlRaceTests
{
    [Test]
    public async Task Independent_contexts_racing_bootstrap_commit_exactly_one_identity()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        var result = await NodeEnrollmentMySqlRaceRunner.CompeteBootstrapAsync(fixture);
        Assert.Multiple(() =>
        {
            Assert.That(result.AcceptedCount, Is.EqualTo(1));
            Assert.That(result.RejectedCount, Is.EqualTo(1));
            Assert.That(result.Committed, Is.True);
            Assert.That(result.HasCredentials, Is.True);
            Assert.That(result.HasCertificatePin, Is.True);
        });
    }

    [Test]
    public async Task Independent_contexts_racing_recovery_consume_one_challenge_once()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var challenge = await fixture.CreateChallengeAsync();
        Assert.That(challenge.Accepted, Is.True);
        var request = fixture.SignChallenge(challenge);
        var result = await NodeEnrollmentMySqlRaceRunner.CompeteRecoveryAsync(fixture, request);
        Assert.Multiple(() =>
        {
            Assert.That(result.AcceptedCount, Is.EqualTo(1));
            Assert.That(result.RejectedCount, Is.EqualTo(1));
            Assert.That(result.Consumed, Is.True);
        });
    }
}

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeEnrollmentMySqlRecoveryStateTests
{
    [Test]
    public async Task Expired_recovery_challenge_is_rejected()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var challenge = await fixture.CreateChallengeAsync();
        var request = fixture.SignChallenge(challenge);
        await using (var update = fixture.CreateContext())
        {
            await update.NodeEnrollmentRecoveryChallenges
                .Where(value => value.Id == challenge.ChallengeId)
                .ExecuteUpdateAsync(values => values.SetProperty(value => value.ExpiresAtUtc, fixture.Now.AddMinutes(-1).UtcDateTime));
        }

        var result = await fixture.CompleteRecoveryAsync(request);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public async Task Revoked_enrollment_cannot_complete_recovery()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var challenge = await fixture.CreateChallengeAsync();
        var request = fixture.SignChallenge(challenge);
        await using (var update = fixture.CreateContext())
        {
            await update.NodeEnrollmentAuthorizations
                .Where(value => value.Id == fixture.EnrollmentId)
                .ExecuteUpdateAsync(values => values.SetProperty(value => value.Status, NodeEnrollmentStatuses.Revoked));
        }

        var result = await fixture.CompleteRecoveryAsync(request);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public async Task Expired_credential_envelope_returns_the_receipt_without_credentials()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var challenge = await fixture.CreateChallengeAsync();
        var request = fixture.SignChallenge(challenge);
        await NodeEnrollmentMySqlStateChanges.ExpireCredentialEnvelopeAsync(fixture);

        var result = await fixture.CompleteRecoveryAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Receipt, Is.Not.Null);
            Assert.That(result.Credentials, Is.Null);
        });
    }
}

internal static class NodeEnrollmentMySqlRaceRunner
{
    public static async Task<NodeEnrollmentBootstrapRaceObservation> CompeteBootstrapAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using (var seed = fixture.CreateContext())
        {
            seed.NodeEnrollmentAuthorizations.Add(fixture.CreatePendingAuthorization());
            await seed.SaveChangesAsync();
        }

        var results = await RunConcurrentlyAsync(
            () => RunBootstrapAsync(fixture),
            () => RunBootstrapAsync(fixture));
        await using var verify = fixture.CreateContext();
        var committed = await verify.NodeEnrollmentAuthorizations.SingleAsync(value => value.Id == fixture.EnrollmentId);
        return new NodeEnrollmentBootstrapRaceObservation(
            results.Count(result => result.Accepted),
            results.Count(result => !result.Accepted),
            committed.Status == NodeEnrollmentStatuses.Committed,
            committed.NodeApiKeySha256 is not null,
            committed.CertificateSha256 is not null);
    }

    public static async Task<NodeEnrollmentRecoveryRaceObservation> CompeteRecoveryAsync(
        NodeEnrollmentMySqlTestFixture fixture,
        NodeRecoveryCompletionRequest request)
    {
        var results = await RunConcurrentlyAsync(
            () => RunRecoveryAsync(fixture, request),
            () => RunRecoveryAsync(fixture, request));
        await using var verify = fixture.CreateContext();
        var storedChallenge = await verify.NodeEnrollmentRecoveryChallenges.SingleAsync(value => value.Id == request.ChallengeId);
        return new NodeEnrollmentRecoveryRaceObservation(
            results.Count(result => result.Accepted),
            results.Count(result => !result.Accepted),
            storedChallenge.ConsumedAtUtc is not null);
    }

    private static async Task<NodeEnrollmentOperationResult> RunBootstrapAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using var db = fixture.CreateContext();
        var service = fixture.CreateService(db);
        return await service.BootstrapAsync(fixture.EnrollmentId, fixture.ValidBootstrapRequest(), CancellationToken.None);
    }

    private static async Task<NodeEnrollmentOperationResult> RunRecoveryAsync(
        NodeEnrollmentMySqlTestFixture fixture,
        NodeRecoveryCompletionRequest request)
    {
        await using var db = fixture.CreateContext();
        var service = fixture.CreateService(db);
        return await service.CompleteRecoveryAsync(fixture.EnrollmentId, request, CancellationToken.None);
    }

    private static async Task<T[]> RunConcurrentlyAsync<T>(Func<Task<T>> firstOperation, Func<Task<T>> secondOperation)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () => { await start.Task; return await firstOperation(); });
        var second = Task.Run(async () => { await start.Task; return await secondOperation(); });
        start.SetResult();
        return await Task.WhenAll(first, second);
    }
}

internal sealed record NodeEnrollmentBootstrapRaceObservation(
    int AcceptedCount,
    int RejectedCount,
    bool Committed,
    bool HasCredentials,
    bool HasCertificatePin);

internal sealed record NodeEnrollmentRecoveryRaceObservation(int AcceptedCount, int RejectedCount, bool Consumed);

internal static class NodeEnrollmentMySqlStateChanges
{
    public static async Task ExpireCredentialEnvelopeAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using var update = fixture.CreateContext();
        await update.NodeEnrollmentAuthorizations
            .Where(value => value.Id == fixture.EnrollmentId)
            .ExecuteUpdateAsync(values => values.SetProperty(value => value.CredentialBundleExpiresAtUtc, fixture.Now.AddMinutes(-1).UtcDateTime));
    }
}
