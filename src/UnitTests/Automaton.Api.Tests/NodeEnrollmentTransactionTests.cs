using Automaton.Api.Entities;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class NodeEnrollmentBootstrapTransactionTests
{
    [Test]
    public async Task Bootstrap_commits_the_pinned_identity_and_credential_bundle_once()
    {
        await using var lab = await NodeEnrollmentTestHarness.CreateAsync();
        var result = await lab.BootstrapAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.HasCredentials, Is.True);
            Assert.That(result.Status, Is.EqualTo(NodeEnrollmentStatuses.Committed));
            Assert.That(result.CertificatePinned, Is.True);
            Assert.That(result.ApiKeyHashStored, Is.True);
            Assert.That(result.CredentialEnvelopeProtected, Is.True);
            Assert.That(result.CommitTimestampStored, Is.True);
            Assert.That(result.ReplayAccepted, Is.False);
            Assert.That(result.CorrectIdentityAccepted, Is.True);
            Assert.That(result.WrongApiKeyAccepted, Is.False);
        });
    }
}

[TestFixture]
public sealed class NodeEnrollmentRecoveryTransactionTests
{
    [Test]
    public async Task Recovery_challenge_is_fresh_single_use_and_returns_the_original_bundle()
    {
        await using var lab = await NodeEnrollmentTestHarness.CreateAsync();
        var result = await lab.RecoverAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ChallengeCreated, Is.True);
            Assert.That(result.OriginalReceiptReturned, Is.True);
            Assert.That(result.OriginalCredentialsReturned, Is.True);
            Assert.That(result.ReplayAccepted, Is.False);
            Assert.That(result.ChallengeConsumed, Is.True);
        });
    }
}

[TestFixture]
public sealed class NodeEnrollmentRejectionTransactionTests
{
    [Test]
    public async Task Foreign_key_proof_is_rejected_without_partial_identity_state()
    {
        await using var lab = await NodeEnrollmentTestHarness.CreateAsync();
        var result = await lab.TryForeignKeyAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Status, Is.EqualTo(NodeEnrollmentStatuses.Pending));
            Assert.That(result.HasNoPartialCredentialState, Is.True);
        });
    }
}
