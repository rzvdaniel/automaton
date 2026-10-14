namespace Automaton.Node.Inventory;

public enum NodeInventoryCompletenessV1
{
    Complete = 1,
    Incomplete = 2
}

public enum NodeResourceKindV1
{
    Container = 1,
    Volume = 2,
    Cgroup = 3,
    StorageDomain = 4
}

public enum NodeInventoryObservationSourceV1
{
    DockerEngine = 1,
    CgroupV2 = 2,
    StorageProbe = 3,
    TestFixture = 4
}

public enum NodeInventoryIncompleteReasonV1
{
    SourceUnavailable = 1,
    PartialEnumeration = 2,
    SourceChangedDuringRead = 3,
    BoundsExceeded = 4,
    CollectionFailed = 5
}

public enum NodeResourceObservedStateV1
{
    Present = 1,
    Running = 2,
    Stopped = 3,
    Unknown = 4
}

public enum NodeResourceOwnerScopeV1
{
    Host = 1,
    Account = 2,
    Workload = 3
}

public sealed record NodeInventorySnapshotV1
{
    private const int MaximumObservationCopyCount = NodeInventoryContractValidator.MaximumObservations + 1;

    private IReadOnlyList<NodeResourceObservationV1> _observations = Array.Empty<NodeResourceObservationV1>();

    public required int SchemaVersion { get; init; }
    public required Guid ControlPlaneRealmId { get; init; }
    public required string RecoveryEpoch { get; init; }
    public required Guid NodeId { get; init; }
    public required Guid InstallationId { get; init; }
    public required string Incarnation { get; init; }
    public required string HostBootIdSha256 { get; init; }
    public required string EngineIdentitySha256 { get; init; }
    public required Guid SnapshotId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required NodeInventoryObservationSourceV1 ObservationSource { get; init; }
    public required NodeInventoryCompletenessV1 Completeness { get; init; }
    public NodeInventoryIncompleteReasonV1? IncompleteReason { get; init; }

    public required IReadOnlyList<NodeResourceObservationV1> Observations
    {
        get => _observations;
        init => _observations = CopyObservations(value);
    }

    public required string SnapshotSha256 { get; init; }

    private static IReadOnlyList<NodeResourceObservationV1> CopyObservations(
        IReadOnlyList<NodeResourceObservationV1>? observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var initialCapacity = Math.Clamp(observations.Count, 0, MaximumObservationCopyCount);
        var copy = new List<NodeResourceObservationV1>(initialCapacity);
        foreach (var observation in observations)
        {
            copy.Add(observation);
            if (copy.Count == MaximumObservationCopyCount)
                break;
        }

        return Array.AsReadOnly(copy.ToArray());
    }
}

public sealed record NodeResourceObservationV1
{
    public required NodeResourceKindV1 ResourceKind { get; init; }
    public required string PhysicalIdSha256 { get; init; }
    public required NodeResourceObservedStateV1 ObservedState { get; init; }
    public required int LabelCount { get; init; }
    public required string LabelSetSha256 { get; init; }
    public required string PhysicalIdentitySha256 { get; init; }
}

public sealed record NodeResourceOwnerBindingV1
{
    public required int SchemaVersion { get; init; }
    public required Guid ControlPlaneRealmId { get; init; }
    public required string RecoveryEpoch { get; init; }
    public required Guid NodeId { get; init; }
    public required Guid InstallationId { get; init; }
    public required NodeResourceOwnerScopeV1 OwnerScope { get; init; }
    public required Guid OwnerId { get; init; }
    public required Guid ResourceId { get; init; }
    public required NodeResourceKindV1 ResourceKind { get; init; }
    public required string OwnerRecordRevision { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? AssignmentId { get; init; }
    public string? AssignmentGeneration { get; init; }
    public string? EffectivePolicyRevision { get; init; }
    public string? EffectivePolicySha256 { get; init; }
    public Guid? WorkloadId { get; init; }
    public string? WorkloadRevision { get; init; }
    public Guid? OperationId { get; init; }
    public Guid? ReservationId { get; init; }
}
