using Automaton.Node.Inventory;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeInventoryContractTests
{
    private static readonly Guid RealmId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid NodeId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid InstallationId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid AccountId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkloadId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId = Guid.Parse("90000000-0000-0000-0000-000000000001");

    [Test]
    public void Snapshot_requires_realm_installation_incarnation_and_well_formed_metadata()
    {
        Assert.DoesNotThrow(() => NodeInventoryContractValidator.ValidateSnapshot(CreateSnapshot()));
        AssertInvalid(CreateSnapshot() with { ControlPlaneRealmId = Guid.Empty });
        AssertInvalid(CreateSnapshot() with { InstallationId = Guid.Empty });
        AssertInvalid(CreateSnapshot() with { RecoveryEpoch = "01" });
        AssertInvalid(CreateSnapshot() with { RecoveryEpoch = "0" });
        AssertInvalid(CreateSnapshot() with { Incarnation = "9223372036854775808" });
        AssertInvalid(CreateSnapshot() with
        {
            ObservedAtUtc = DateTimeOffset.Parse("2026-10-14T12:00:00+01:00",
                System.Globalization.CultureInfo.InvariantCulture)
        });
        AssertInvalid(CreateSnapshot() with { SnapshotSha256 = new string('A', 64) });
        AssertInvalid(CreateSnapshot() with { EngineIdentitySha256 = "not-a-digest" });
    }

    [Test]
    public void Snapshot_and_observation_counts_are_bounded()
    {
        var tooMany = Enumerable.Range(0, NodeInventoryContractValidator.MaximumObservations + 1)
            .Select(index => CreateObservation(index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        AssertInvalid(CreateSnapshot() with { Observations = tooMany });
        AssertInvalid(CreateSnapshot() with
        {
            Observations = [CreateObservation() with { LabelCount = NodeInventoryContractValidator.MaximumLabelsPerResource + 1 }]
        });
    }

    [Test]
    public void Snapshot_and_observation_copy_caller_collections_and_expose_read_only_views()
    {
        var observation = CreateObservation();
        var observations = new List<NodeResourceObservationV1> { observation };
        var snapshot = CreateSnapshot() with { Observations = observations };

        observations.Clear();

        Assert.That(snapshot.Observations, Has.Count.EqualTo(1));
        Assert.That(snapshot.Observations[0].PhysicalIdSha256, Is.EqualTo(new string('a', 64)));
    }

    [Test]
    public void Host_account_and_workload_owner_scopes_require_disjoint_complete_tuples()
    {
        Assert.DoesNotThrow(() => NodeInventoryContractValidator.ValidateOwnerBinding(CreateHostBinding()));
        Assert.DoesNotThrow(() => NodeInventoryContractValidator.ValidateOwnerBinding(CreateAccountBinding()));
        Assert.DoesNotThrow(() => NodeInventoryContractValidator.ValidateOwnerBinding(CreateWorkloadBinding()));

        AssertInvalid(CreateHostBinding() with { OwnerId = AccountId });
        AssertInvalid(CreateHostBinding() with { AccountId = AccountId });
        AssertInvalid(CreateAccountBinding() with { AssignmentGeneration = null });
        AssertInvalid(CreateAccountBinding() with { EffectivePolicySha256 = null });
        AssertInvalid(CreateAccountBinding() with { WorkloadId = WorkloadId });
        AssertInvalid(CreateWorkloadBinding() with { OperationId = null });
        AssertInvalid(CreateWorkloadBinding() with { ReservationId = Guid.Empty });
        AssertInvalid(CreateWorkloadBinding() with { OwnerId = AccountId });
    }

    [Test]
    public void Label_count_and_all_digest_shapes_are_bounded()
    {
        AssertInvalid(CreateSnapshot() with { HostBootIdSha256 = "C:\\private\\host" });
        AssertInvalid(CreateSnapshot() with { EngineIdentitySha256 = "not-a-digest" });
        AssertInvalid(CreateSnapshot() with
        {
            Observations = [CreateObservation() with { LabelSetSha256 = "synthetic secret" }]
        });
        AssertInvalid(CreateSnapshot() with
        {
            Observations = [CreateObservation() with { PhysicalIdentitySha256 = "C:\\tenant\\volume" }]
        });
        AssertInvalid(CreateSnapshot() with
        {
            Observations = [CreateObservation() with { PhysicalIdSha256 = new string('A', 64) }]
        });
        AssertInvalid(CreateSnapshot() with
        {
            Observations = [CreateObservation() with { LabelCount = -1 }]
        });
        AssertInvalid(CreateAccountBinding() with { EffectivePolicySha256 = "not-a-digest" });
        AssertInvalid(CreateAccountBinding() with { OwnerRecordRevision = "01" });
    }

    [Test]
    public void Closed_enums_and_incomplete_reason_are_validated()
    {
        var incomplete = CreateSnapshot() with
        {
            Completeness = NodeInventoryCompletenessV1.Incomplete,
            IncompleteReason = NodeInventoryIncompleteReasonV1.BoundsExceeded
        };
        Assert.DoesNotThrow(() => NodeInventoryContractValidator.ValidateSnapshot(incomplete));
        AssertInvalid(CreateSnapshot() with { Completeness = NodeInventoryCompletenessV1.Incomplete });
        AssertInvalid(CreateSnapshot() with
        {
            IncompleteReason = NodeInventoryIncompleteReasonV1.BoundsExceeded
        });
        AssertInvalid(CreateSnapshot() with { ObservationSource = (NodeInventoryObservationSourceV1)99 });
        AssertInvalid(CreateSnapshot() with { Completeness = (NodeInventoryCompletenessV1)99 });
        AssertInvalid(CreateSnapshot() with
        {
            Completeness = NodeInventoryCompletenessV1.Incomplete,
            IncompleteReason = (NodeInventoryIncompleteReasonV1)99
        });
        AssertInvalid(CreateSnapshot() with
        {
            Observations = [CreateObservation() with { ObservedState = (NodeResourceObservedStateV1)99 }]
        });
        AssertInvalid(CreateSnapshot() with
        {
            Observations = [CreateObservation() with { ResourceKind = (NodeResourceKindV1)99 }]
        });
    }

    [Test]
    public void Portable_models_contain_no_authority_action_or_raw_sensitive_fields()
    {
        var modelTypes = new[]
        {
            typeof(NodeInventorySnapshotV1),
            typeof(NodeResourceObservationV1),
            typeof(NodeResourceOwnerBindingV1)
        };
        var forbiddenPropertyFragments = new[] { "Password", "Credential", "ApiKey", "Certificate", "Path", "Action", "Permission", "Grant", "Raw" };

        foreach (var property in modelTypes.SelectMany(type => type.GetProperties()))
        {
            Assert.That(forbiddenPropertyFragments.Any(fragment => property.Name.Contains(fragment,
                StringComparison.OrdinalIgnoreCase)), Is.False, property.Name);
            Assert.That(property.Name, Is.Not.EqualTo("Labels"));
            Assert.That(property.Name, Is.Not.EqualTo("PhysicalId"));
        }

        Assert.That(typeof(NodeInventorySnapshotV1).GetProperties().Any(property => property.Name == "Assessment"), Is.False);
        Assert.That(typeof(NodeResourceObservationV1).GetProperties().Any(property => property.Name == "Action"), Is.False);
        Assert.That(typeof(NodeInventorySnapshotV1).GetProperty("ObservationSource"), Is.Not.Null);
        Assert.That(typeof(NodeInventorySnapshotV1).GetProperty("IncompleteReason"), Is.Not.Null);
    }

    private static NodeInventorySnapshotV1 CreateSnapshot() => new()
    {
        SchemaVersion = 1,
        ControlPlaneRealmId = RealmId,
        RecoveryEpoch = "3",
        NodeId = NodeId,
        InstallationId = InstallationId,
        Incarnation = "1",
        HostBootIdSha256 = new string('e', 64),
        EngineIdentitySha256 = new string('a', 64),
        SnapshotId = Guid.Parse("a0000000-0000-0000-0000-000000000001"),
        ObservedAtUtc = DateTimeOffset.Parse("2026-10-14T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        ObservationSource = NodeInventoryObservationSourceV1.TestFixture,
        Completeness = NodeInventoryCompletenessV1.Complete,
        Observations = [CreateObservation()],
        SnapshotSha256 = new string('b', 64)
    };

    private static NodeResourceObservationV1 CreateObservation(string? physicalIdHash = null) => new()
    {
        ResourceKind = NodeResourceKindV1.Container,
        PhysicalIdSha256 = physicalIdHash ?? new string('a', 64),
        ObservedState = NodeResourceObservedStateV1.Running,
        LabelCount = 1,
        LabelSetSha256 = new string('b', 64),
        PhysicalIdentitySha256 = new string('c', 64)
    };

    private static NodeResourceOwnerBindingV1 CreateHostBinding() => new()
    {
        SchemaVersion = 1,
        ControlPlaneRealmId = RealmId,
        RecoveryEpoch = "3",
        NodeId = NodeId,
        InstallationId = InstallationId,
        OwnerScope = NodeResourceOwnerScopeV1.Host,
        OwnerId = NodeId,
        ResourceId = ResourceId,
        ResourceKind = NodeResourceKindV1.Cgroup,
        OwnerRecordRevision = "1"
    };

    private static NodeResourceOwnerBindingV1 CreateAccountBinding() => CreateHostBinding() with
    {
        OwnerScope = NodeResourceOwnerScopeV1.Account,
        OwnerId = AccountId,
        AccountId = AccountId,
        AssignmentId = AssignmentId,
        AssignmentGeneration = "1",
        EffectivePolicyRevision = "1",
        EffectivePolicySha256 = new string('d', 64)
    };

    private static NodeResourceOwnerBindingV1 CreateWorkloadBinding() => CreateAccountBinding() with
    {
        OwnerScope = NodeResourceOwnerScopeV1.Workload,
        OwnerId = WorkloadId,
        WorkloadId = WorkloadId,
        WorkloadRevision = "1",
        OperationId = Guid.Parse("b0000000-0000-0000-0000-000000000001"),
        ReservationId = Guid.Parse("c0000000-0000-0000-0000-000000000001")
    };

    private static void AssertInvalid(NodeInventorySnapshotV1 snapshot)
        => Assert.That(() => NodeInventoryContractValidator.ValidateSnapshot(snapshot), Throws.TypeOf<InvalidDataException>());

    private static void AssertInvalid(NodeResourceOwnerBindingV1 binding)
        => Assert.That(() => NodeInventoryContractValidator.ValidateOwnerBinding(binding), Throws.TypeOf<InvalidDataException>());
}
