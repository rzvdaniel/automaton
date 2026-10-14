using Automaton.Node.Diagnostics;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeDoctorFormatterTests
{
    private static readonly object[] ExpectedMappings =
    [
        Case(NodeDoctorCode.PlatformLinuxX64Reported, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Information, "Reported platform profile: Linux x64."),
        Case(NodeDoctorCode.PlatformUnsupportedOs, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported operating system is outside the supported Node profile."),
        Case(NodeDoctorCode.PlatformUnsupportedArchitecture, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported architecture is outside the supported Node profile."),
        Case(NodeDoctorCode.PlatformUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The platform profile is unknown."),
        Case(NodeDoctorCode.InstallationLockUnavailable, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The installation lock is unavailable."),
        Case(NodeDoctorCode.IdentityMissing, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The protected Node identity is missing."),
        Case(NodeDoctorCode.IdentityInvalid, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The protected Node identity is invalid."),
        Case(NodeDoctorCode.IdentityStale, NodeDoctorFreshness.Stale, NodeDoctorSeverity.Blocking, "The protected Node identity is stale."),
        Case(NodeDoctorCode.IdentityUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The protected Node identity state is unknown."),
        Case(NodeDoctorCode.RecoveryAuthorityMissing, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "Recovery authority evidence is missing."),
        Case(NodeDoctorCode.RecoveryAuthorityStale, NodeDoctorFreshness.Stale, NodeDoctorSeverity.Blocking, "Recovery authority evidence is stale."),
        Case(NodeDoctorCode.RecoveryAuthorityUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "Recovery authority state is unknown."),
        Case(NodeDoctorCode.RecoveryEpochMismatch, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The recovery epoch does not match the current authority."),
        Case(NodeDoctorCode.CredentialMissing, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The Node credential is missing."),
        Case(NodeDoctorCode.CredentialRevoked, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node credential is revoked."),
        Case(NodeDoctorCode.CredentialStale, NodeDoctorFreshness.Stale, NodeDoctorSeverity.Blocking, "The Node credential status is stale."),
        Case(NodeDoctorCode.CredentialUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The Node credential status is unknown."),
        Case(NodeDoctorCode.ActivationRecoveryRequired, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "Node activation requires recovery."),
        Case(NodeDoctorCode.ActivationFenced, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node activation is fenced."),
        Case(NodeDoctorCode.ActivationUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The Node activation state is unknown."),
        Case(NodeDoctorCode.JournalUnsettled, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "A durable Node operation remains unsettled."),
        Case(NodeDoctorCode.JournalUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The durable Node operation state is unknown."),
        Case(NodeDoctorCode.PackageUnverified, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "Node package provenance is unverified."),
        Case(NodeDoctorCode.PackageInvalid, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node package failed verification."),
        Case(NodeDoctorCode.ServiceUnavailable, NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node service is unavailable."),
        Case(NodeDoctorCode.EngineUnsupported, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported container engine is unsupported."),
        Case(NodeDoctorCode.EngineUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The container engine state is unknown."),
        Case(NodeDoctorCode.CgroupUnsupported, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported cgroup profile is unsupported."),
        Case(NodeDoctorCode.CgroupUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The cgroup profile is unknown."),
        Case(NodeDoctorCode.StorageUnsupported, NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported storage profile is unsupported."),
        Case(NodeDoctorCode.StorageUnknown, NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The storage profile is unknown.")
    ];

    [TestCaseSource(nameof(ExpectedMappings))]
    public void FormatsEveryContractCodeToItsExactDiagnostic(
        NodeDoctorCode inputCode, NodeDoctorFreshness freshness, NodeDoctorSeverity severity, string message)
    {
        var diagnostic = NodeDoctorFormatter.Format(Input(new NodeDoctorFactV1(inputCode, freshness, null))).Single();

        Assert.That(diagnostic.OutputCode, Is.EqualTo(inputCode));
        Assert.That(diagnostic.Severity, Is.EqualTo(severity));
        Assert.That(diagnostic.Freshness, Is.EqualTo(freshness));
        Assert.That(diagnostic.ObservedAtUtc, Is.Null);
        Assert.That(diagnostic.Message, Is.EqualTo(message));
    }

    [Test]
    public void SortsCodesOrdinallyAndCopiesInputAndOutputCollections()
    {
        var facts = new List<NodeDoctorFactV1>
        {
            new(NodeDoctorCode.StorageUnknown, NodeDoctorFreshness.Unknown, null),
            new(NodeDoctorCode.ActivationFenced, NodeDoctorFreshness.Current, null)
        };
        var input = Input(facts);
        facts.Clear();
        var result = NodeDoctorFormatter.Format(input);

        Assert.That(result.Select(item => item.OutputCode), Is.EqualTo(new[]
        {
            NodeDoctorCode.ActivationFenced, NodeDoctorCode.StorageUnknown
        }));
        Assert.That(input.Facts, Has.Count.EqualTo(2));
        Assert.That(result, Is.InstanceOf<IList<NodeDoctorDiagnosticV1>>());
        Assert.That(((IList<NodeDoctorDiagnosticV1>)result).IsReadOnly, Is.True);
    }

    [Test]
    public void EmptyInputProducesNeutralUnknownWithoutReadinessClaim()
    {
        var diagnostic = NodeDoctorFormatter.Format(Input(Array.Empty<NodeDoctorFactV1>())).Single();

        Assert.That(diagnostic.OutputCode, Is.EqualTo(NodeDoctorCode.NoObservationsSupplied));
        Assert.That(diagnostic.Severity, Is.EqualTo(NodeDoctorSeverity.Information));
        Assert.That(diagnostic.Freshness, Is.EqualTo(NodeDoctorFreshness.Unknown));
        Assert.That(diagnostic.Message, Is.EqualTo("No diagnostic observations were supplied."));
    }

    [Test]
    public void RejectsUnsupportedSchemaUndefinedCodesFreshnessDuplicatesAndInvalidTimes()
    {
        Assert.That(() => NodeDoctorFormatter.Format(new(2, Array.Empty<NodeDoctorFactV1>())), Throws.ArgumentException);
        Assert.That(() => NodeDoctorFormatter.Format(Input(new NodeDoctorFactV1((NodeDoctorCode)999, NodeDoctorFreshness.Unknown, null))),
            Throws.ArgumentException);
        Assert.That(() => NodeDoctorFormatter.Format(Input(new NodeDoctorFactV1(NodeDoctorCode.IdentityStale, NodeDoctorFreshness.Current, null))),
            Throws.ArgumentException);
        Assert.That(() => NodeDoctorFormatter.Format(Input(
            new NodeDoctorFactV1(NodeDoctorCode.IdentityMissing, NodeDoctorFreshness.NotApplicable, null),
            new NodeDoctorFactV1(NodeDoctorCode.IdentityMissing, NodeDoctorFreshness.NotApplicable, null))), Throws.ArgumentException);
        Assert.That(() => NodeDoctorFormatter.Format(Input(new NodeDoctorFactV1(NodeDoctorCode.ActivationFenced,
            NodeDoctorFreshness.Current, new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(1))))),
            Throws.ArgumentException);
    }

    [Test]
    public void RejectsMoreThanContractMaximumFactsBeforeFormatting()
    {
        var facts = Enumerable.Repeat(new NodeDoctorFactV1(NodeDoctorCode.ActivationFenced,
            NodeDoctorFreshness.Current, null), NodeDoctorInputV1.MaximumFacts + 1);

        Assert.That(() => new NodeDoctorInputV1(NodeDoctorInputV1.CurrentSchemaVersion, facts), Throws.ArgumentException);
    }

    [Test]
    public void PreservesUtcObservationTimeAndInputCannotCarryFreeFormSensitiveText()
    {
        var time = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var diagnostics = NodeDoctorFormatter.Format(Input(
            new NodeDoctorFactV1(NodeDoctorCode.RecoveryAuthorityUnknown, NodeDoctorFreshness.Unknown, time)));
        var rendered = string.Join("|", diagnostics.Select(d => $"{d.OutputCode}|{d.Message}"));

        Assert.That(diagnostics.Single().ObservedAtUtc, Is.EqualTo(time));
        Assert.That(rendered, Does.Not.Contain("C:\\secret"));
        Assert.That(rendered, Does.Not.Contain("sentinel-token"));
        Assert.That(typeof(NodeDoctorFactV1).GetProperties().Any(property => property.PropertyType == typeof(string)), Is.False);
        Assert.That(typeof(NodeDoctorInputV1).GetProperties().Any(property => property.PropertyType == typeof(string)), Is.False);
    }

    private static object[] Case(NodeDoctorCode code, NodeDoctorFreshness freshness,
        NodeDoctorSeverity severity, string message) => [code, freshness, severity, message];

    private static NodeDoctorInputV1 Input(params NodeDoctorFactV1[] facts) =>
        new(NodeDoctorInputV1.CurrentSchemaVersion, facts);

    private static NodeDoctorInputV1 Input(IEnumerable<NodeDoctorFactV1> facts) =>
        new(NodeDoctorInputV1.CurrentSchemaVersion, facts);
}
