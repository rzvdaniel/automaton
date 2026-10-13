using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationSameRequestMySqlTests
{
    [Test]
    public async Task Independent_contexts_racing_identical_initial_request_return_one_immutable_receipt()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var request = HostFenceTestSupport.CreateRequest(fixture, Guid.NewGuid(), 0);
        var results = await RaceAsync(
            () => HostFenceTestSupport.ActivateInOwnContextAsync(fixture, request),
            () => HostFenceTestSupport.ActivateInOwnContextAsync(fixture, request));
        var state = await HostFenceTestSupport.LoadManagementAsync(fixture);
        var attempts = await HostFenceTestSupport.CountAttemptsAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(results.All(value => value.Disposition == NodeActivationDisposition.Accepted), Is.True);
            Assert.That(results[0].ImmutableReceiptJson, Is.EqualTo(results[1].ImmutableReceiptJson));
            Assert.That(state.CurrentIncarnation, Is.EqualTo(1));
            Assert.That(attempts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Independent_contexts_racing_same_restart_request_consume_one_hfa_receipt_once()
    {
        Assert.That(await NodeActivationMySqlRaceScenarios.SameRestartRaceIsValidAsync(), Is.True);
    }

    private static async Task<T[]> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[]
        {
            Task.Run(async () => { await gate.Task; return await first(); }),
            Task.Run(async () => { await gate.Task; return await second(); })
        };
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationPositiveIncarnationRaceMySqlTests
{
    [Test]
    public async Task Independent_requests_racing_the_same_positive_incarnation_advance_once()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var initial = HostFenceTestSupport.CreateRequest(fixture, Guid.NewGuid(), 0);
        Assert.That((await HostFenceTestSupport.ActivateInOwnContextAsync(fixture, initial)).Disposition,
            Is.EqualTo(NodeActivationDisposition.Accepted));
        await HostFenceTestSupport.SetFencedStateAsync(fixture, initial.RecoveryCheckpointSha256);

        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var requests = new[]
        {
            CreateRestartRequest(fixture, initial, Guid.NewGuid(), signer),
            CreateRestartRequest(fixture, initial, Guid.NewGuid(), signer)
        };
        var validator = new NodeHostFenceReceiptValidator(new MySqlHostFenceTestTrustStore(signer));
        var results = await RaceAsync(
            () => HostFenceTestSupport.ActivateInOwnContextAsync(fixture, requests[0], validator),
            () => HostFenceTestSupport.ActivateInOwnContextAsync(fixture, requests[1], validator));
        var state = await HostFenceTestSupport.LoadManagementAsync(fixture);
        var attempts = await HostFenceTestSupport.CountAttemptsAsync(fixture);
        var uses = await HostFenceTestSupport.CountFenceUsesAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(value => value.Disposition == NodeActivationDisposition.Accepted), Is.EqualTo(1));
            Assert.That(results.Count(value => value.Disposition == NodeActivationDisposition.Conflict), Is.EqualTo(1));
            Assert.That(state.CurrentIncarnation, Is.EqualTo(2));
            Assert.That(state.HostFenceGeneration, Is.EqualTo(1));
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(uses, Is.EqualTo(1));
        });
    }

    private static NodeActivationRequest CreateRestartRequest(NodeEnrollmentMySqlTestFixture fixture,
        NodeActivationRequest initial, Guid requestId, ECDsa signer)
    {
        var request = HostFenceTestSupport.CreateRequest(fixture, requestId, 1);
        var payload = HostFenceTestSupport.CreateFencePayload(fixture, initial, request, 1);
        return request with { HostFenceReceipt = NodeActivationContractTests.Sign(payload, signer) };
    }

    private static async Task<T[]> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[]
        {
            Task.Run(async () => { await gate.Task; return await first(); }),
            Task.Run(async () => { await gate.Task; return await second(); })
        };
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationRevocationRaceMySqlTests
{
    [Test]
    public async Task Revocation_after_preflight_in_an_independent_context_blocks_activation_cas()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        var observation = await NodeActivationMySqlRaceScenarios.RevokeAfterPreflightAsync(fixture);
        Assert.Multiple(() =>
        {
            Assert.That(observation.Disposition, Is.EqualTo(NodeActivationDisposition.Conflict));
            Assert.That(observation.EnrollmentStatus, Is.EqualTo(NodeEnrollmentStatuses.Revoked));
            Assert.That(observation.ManagementRows, Is.Zero);
            Assert.That(observation.AttemptRows, Is.Zero);
        });
    }

}

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationRollbackMySqlTests
{
    [Test]
    public async Task Rollback_preserves_incarnation_and_fence_receipt_for_retry_then_consumes_once()
    {
        Assert.That(await NodeActivationMySqlRaceScenarios.RollbackCaseIsValidAsync(), Is.True);
    }
}

internal static class NodeActivationMySqlRaceScenarios
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<bool> SameRestartRaceIsValidAsync()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        var result = await RaceSameRestartRequestAsync(fixture);
        return result.AllAccepted && result.SameReceipt && result.ReceiptUses == 1 && result.AttemptCount == 2;
    }

    public static async Task<bool> RollbackCaseIsValidAsync()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        var result = await RollbackAndRetryAsync(fixture);
        return result.RollbackDisposition == NodeActivationDisposition.Conflict &&
            result.IncarnationAfterRollback == 1 && result.FenceGenerationAfterRollback == 0 &&
            result.AttemptsAfterRollback == 1 && result.UsesAfterRollback == 0 &&
            result.RetryDisposition == NodeActivationDisposition.Accepted && result.RetryReceiptIsImmutable &&
            result.IncarnationAfterRetry == 2 && result.UsesAfterRetry == 1;
    }

    public static async Task<SameRequestRaceObservation> RaceSameRestartRequestAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var prepared = await PrepareFencedRestartAsync(fixture, signer);
        var results = await RaceAsync(
            () => HostFenceTestSupport.ActivateInOwnContextAsync(fixture, prepared.Request, prepared.Validator),
            () => HostFenceTestSupport.ActivateInOwnContextAsync(fixture, prepared.Request, prepared.Validator));
        return new SameRequestRaceObservation(results.All(value => value.Disposition == NodeActivationDisposition.Accepted),
            results[0].ImmutableReceiptJson == results[1].ImmutableReceiptJson,
            await HostFenceTestSupport.CountFenceUsesAsync(fixture), await HostFenceTestSupport.CountAttemptsAsync(fixture));
    }

    public static async Task<RevocationRaceObservation> RevokeAfterPreflightAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await fixture.CommitEnrollmentAsync();
        var request = HostFenceTestSupport.CreateRequest(fixture, Guid.NewGuid(), 0);
        var result = await ActivateAfterRevocationAsync(fixture, request);
        return await ReadRevocationStateAsync(fixture, result);
    }

    public static async Task<RollbackRaceObservation> RollbackAndRetryAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var prepared = await PrepareFencedRestartAsync(fixture, signer);
        var rollback = await ActivateWithSaveFailureAsync(fixture, prepared);
        var rollbackState = await ReadRollbackStateAsync(fixture);
        var retry = await HostFenceTestSupport.ActivateInOwnContextAsync(fixture, prepared.Request, prepared.Validator);
        var replay = await HostFenceTestSupport.ActivateInOwnContextAsync(fixture, prepared.Request, prepared.Validator);
        var committed = await HostFenceTestSupport.LoadManagementAsync(fixture);
        return new RollbackRaceObservation(rollback.Disposition, rollbackState.Incarnation, rollbackState.FenceGeneration,
            rollbackState.Attempts, rollbackState.ReceiptUses, retry.Disposition,
            retry.ImmutableReceiptJson == replay.ImmutableReceiptJson, committed.CurrentIncarnation ?? 0,
            await HostFenceTestSupport.CountFenceUsesAsync(fixture));
    }

    private static async Task<PreparedRestart> PrepareFencedRestartAsync(NodeEnrollmentMySqlTestFixture fixture, ECDsa signer)
    {
        await fixture.CommitEnrollmentAsync();
        var initial = HostFenceTestSupport.CreateRequest(fixture, Guid.NewGuid(), 0);
        var accepted = await HostFenceTestSupport.ActivateInOwnContextAsync(fixture, initial);
        if (accepted.Disposition != NodeActivationDisposition.Accepted)
            throw new InvalidOperationException("Initial activation setup must succeed.");
        await HostFenceTestSupport.SetFencedStateAsync(fixture, initial.RecoveryCheckpointSha256);
        var request = HostFenceTestSupport.CreateRequest(fixture, Guid.NewGuid(), 1);
        var payload = HostFenceTestSupport.CreateFencePayload(fixture, initial, request, 1);
        request = request with { HostFenceReceipt = NodeActivationContractTests.Sign(payload, signer) };
        return new PreparedRestart(request, new NodeHostFenceReceiptValidator(new MySqlHostFenceTestTrustStore(signer)));
    }

    private static async Task<NodeActivationResult> ActivateAfterRevocationAsync(
        NodeEnrollmentMySqlTestFixture fixture, NodeActivationRequest request)
    {
        var epochAuthority = new RevokeAfterPreflightEpochAuthority(fixture);
        await using var db = fixture.CreateContext();
        return await fixture.CreateActivationService(db, epochAuthority: epochAuthority)
            .ActivateAsync(HostFenceTestSupport.Identity(fixture), request,
                HostFenceTestSupport.Canonicalize(request), CancellationToken.None);
    }

    private static async Task<RevocationRaceObservation> ReadRevocationStateAsync(
        NodeEnrollmentMySqlTestFixture fixture, NodeActivationResult result)
    {
        await using var db = fixture.CreateContext();
        var enrollment = await db.NodeEnrollmentAuthorizations.SingleAsync(value => value.Id == fixture.EnrollmentId);
        return new RevocationRaceObservation(result.Disposition, enrollment.Status,
            await db.NodeManagement.CountAsync(), await db.NodeActivationAttempts.CountAsync());
    }

    private static async Task<NodeActivationResult> ActivateWithSaveFailureAsync(
        NodeEnrollmentMySqlTestFixture fixture, PreparedRestart prepared)
    {
        await using var db = fixture.CreateContext(new FailHostFenceAttemptSaveInterceptor());
        return await fixture.CreateActivationService(db, prepared.Validator)
            .ActivateAsync(HostFenceTestSupport.Identity(fixture), prepared.Request,
                HostFenceTestSupport.Canonicalize(prepared.Request), CancellationToken.None);
    }

    private static async Task<RollbackState> ReadRollbackStateAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        var management = await HostFenceTestSupport.LoadManagementAsync(fixture);
        return new RollbackState(management.CurrentIncarnation ?? 0, management.HostFenceGeneration,
            await HostFenceTestSupport.CountAttemptsAsync(fixture), await HostFenceTestSupport.CountFenceUsesAsync(fixture));
    }

    private static async Task<T[]> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[]
        {
            Task.Run(async () => { await gate.Task; return await first(); }),
            Task.Run(async () => { await gate.Task; return await second(); })
        };
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    private sealed class RevokeAfterPreflightEpochAuthority(NodeEnrollmentMySqlTestFixture fixture) : INodeRecoveryEpochAuthority
    {
        public async Task<bool> IsCurrentEpochAsync(Guid realmId, long recoveryEpoch, CancellationToken cancellationToken)
        {
            await using var db = fixture.CreateContext();
            await db.NodeEnrollmentAuthorizations.Where(value => value.Id == fixture.EnrollmentId)
                .ExecuteUpdateAsync(updates => updates.SetProperty(value => value.Status, NodeEnrollmentStatuses.Revoked), cancellationToken);
            return true;
        }
    }

    private sealed class FailHostFenceAttemptSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<NodeActivationAttempt>()
                .Any(value => value.State == EntityState.Added) == true)
                return ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Synthetic test failure before activation attempt insert."));
            return ValueTask.FromResult(result);
        }
    }

    private sealed record PreparedRestart(NodeActivationRequest Request, INodeHostFenceReceiptValidator Validator);
    private sealed record RollbackState(long Incarnation, long FenceGeneration, int Attempts, int ReceiptUses);
}

internal sealed record SameRequestRaceObservation(bool AllAccepted, bool SameReceipt, int ReceiptUses, int AttemptCount);
internal sealed record RevocationRaceObservation(NodeActivationDisposition Disposition, string EnrollmentStatus,
    int ManagementRows, int AttemptRows);
internal sealed record RollbackRaceObservation(NodeActivationDisposition RollbackDisposition, long IncarnationAfterRollback,
    long FenceGenerationAfterRollback, int AttemptsAfterRollback, int UsesAfterRollback,
    NodeActivationDisposition RetryDisposition, bool RetryReceiptIsImmutable, long IncarnationAfterRetry, int UsesAfterRetry);
