using System.Globalization;

namespace Automaton.Node.Inventory;

public static class NodeInventoryContractValidator
{
    public const int MaximumObservations = 4_096;
    public const int MaximumLabelsPerResource = 64;

    public static void ValidateSnapshot(NodeInventorySnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshotIdentity(snapshot);
        ValidateSnapshotMetadata(snapshot);
        ValidateSnapshotObservations(snapshot);
    }

    public static void ValidateOwnerBinding(NodeResourceOwnerBindingV1 binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ValidateOwnerBindingIdentity(binding);
        ValidateOwnerScope(binding);
    }

    private static void ValidateSnapshotIdentity(NodeInventorySnapshotV1 snapshot)
    {
        if (snapshot.ControlPlaneRealmId == Guid.Empty
            || snapshot.NodeId == Guid.Empty
            || snapshot.InstallationId == Guid.Empty
            || snapshot.SnapshotId == Guid.Empty
            || !IsPositiveInt64(snapshot.RecoveryEpoch)
            || !IsPositiveInt64(snapshot.Incarnation))
        {
            throw Invalid("The inventory snapshot has invalid realm, Node or incarnation identity.");
        }
    }

    private static void ValidateSnapshotMetadata(NodeInventorySnapshotV1 snapshot)
    {
        if (snapshot.SchemaVersion != 1
            || !IsLowerSha256(snapshot.HostBootIdSha256)
            || !IsLowerSha256(snapshot.EngineIdentitySha256)
            || snapshot.ObservedAtUtc.Offset != TimeSpan.Zero
            || !Enum.IsDefined(snapshot.ObservationSource)
            || !Enum.IsDefined(snapshot.Completeness)
            || !IsValidIncompleteReason(snapshot)
            || !IsLowerSha256(snapshot.SnapshotSha256))
        {
            throw Invalid("The inventory snapshot has invalid version, bounds or metadata.");
        }
    }

    private static void ValidateSnapshotObservations(NodeInventorySnapshotV1 snapshot)
    {
        if (snapshot.Observations is null || snapshot.Observations.Count > MaximumObservations)
            throw Invalid("The inventory observation collection is missing or exceeds its item limit.");

        var physicalIds = new HashSet<(NodeResourceKindV1 Kind, string Id)>();
        foreach (var observation in snapshot.Observations)
        {
            ValidateObservation(observation);
            if (!physicalIds.Add((observation.ResourceKind, observation.PhysicalIdSha256)))
                throw Invalid("The snapshot contains duplicate physical resource identities.");
        }
    }

    private static void ValidateOwnerBindingIdentity(NodeResourceOwnerBindingV1 binding)
    {
        if (binding.SchemaVersion != 1
            || binding.ControlPlaneRealmId == Guid.Empty
            || binding.NodeId == Guid.Empty
            || binding.InstallationId == Guid.Empty
            || binding.OwnerId == Guid.Empty
            || binding.ResourceId == Guid.Empty
            || !IsPositiveInt64(binding.RecoveryEpoch)
            || !IsPositiveInt64(binding.OwnerRecordRevision)
            || !Enum.IsDefined(binding.OwnerScope)
            || !Enum.IsDefined(binding.ResourceKind))
        {
            throw Invalid("The owner binding has invalid identity or metadata.");
        }
    }

    private static void ValidateOwnerScope(NodeResourceOwnerBindingV1 binding)
    {
        switch (binding.OwnerScope)
        {
            case NodeResourceOwnerScopeV1.Host:
                ValidateHostOwner(binding);
                return;
            case NodeResourceOwnerScopeV1.Account:
                ValidateAccountOwner(binding);
                return;
            case NodeResourceOwnerScopeV1.Workload:
                ValidateWorkloadOwner(binding);
                return;
            default:
                throw Invalid("The owner binding scope is not supported.");
        }
    }

    private static void ValidateHostOwner(NodeResourceOwnerBindingV1 binding)
    {
        if (binding.OwnerId != binding.NodeId)
            throw Invalid("A host-scoped owner must use the Node identity.");
        RequireNoAccountOrWorkload(binding);
    }

    private static void ValidateAccountOwner(NodeResourceOwnerBindingV1 binding)
    {
        if (binding.AccountId != binding.OwnerId)
            throw Invalid("An account-scoped owner must use the account identity.");
        RequireAccount(binding);
        RequireNoWorkload(binding);
    }

    private static void ValidateWorkloadOwner(NodeResourceOwnerBindingV1 binding)
    {
        if (binding.WorkloadId != binding.OwnerId)
            throw Invalid("A workload-scoped owner must use the workload identity.");
        RequireAccount(binding);
        if (binding.WorkloadId == Guid.Empty
            || !IsPositiveInt64(binding.WorkloadRevision)
            || binding.OperationId is null
            || binding.OperationId == Guid.Empty
            || binding.ReservationId is null
            || binding.ReservationId == Guid.Empty)
        {
            throw Invalid("A workload owner requires its revision, operation and reservation identities.");
        }
    }

    private static void ValidateObservation(NodeResourceObservationV1 observation)
    {
        if (observation is null
            || !Enum.IsDefined(observation.ResourceKind)
            || !IsLowerSha256(observation.PhysicalIdSha256)
            || !Enum.IsDefined(observation.ObservedState)
            || observation.LabelCount < 0
            || observation.LabelCount > MaximumLabelsPerResource
            || !IsLowerSha256(observation.LabelSetSha256)
            || !IsLowerSha256(observation.PhysicalIdentitySha256))
        {
            throw Invalid("The resource observation has invalid identity, bounds or metadata.");
        }
    }

    private static bool IsValidIncompleteReason(NodeInventorySnapshotV1 snapshot)
        => snapshot.Completeness == NodeInventoryCompletenessV1.Complete
            ? snapshot.IncompleteReason is null
            : snapshot.IncompleteReason is not null && Enum.IsDefined(snapshot.IncompleteReason.Value);

    private static void RequireAccount(NodeResourceOwnerBindingV1 binding)
    {
        if (binding.AccountId is null
            || binding.AccountId == Guid.Empty
            || binding.AssignmentId is null
            || binding.AssignmentId == Guid.Empty
            || !IsPositiveInt64(binding.AssignmentGeneration)
            || !IsPositiveInt64(binding.EffectivePolicyRevision)
            || !IsLowerSha256(binding.EffectivePolicySha256))
        {
            throw Invalid("An account-scoped owner requires account, assignment and effective policy identity.");
        }
    }

    private static void RequireNoAccountOrWorkload(NodeResourceOwnerBindingV1 binding)
    {
        if (binding.AccountId is not null
            || binding.AssignmentId is not null
            || binding.AssignmentGeneration is not null
            || binding.EffectivePolicyRevision is not null
            || binding.EffectivePolicySha256 is not null
            || binding.WorkloadId is not null
            || binding.WorkloadRevision is not null
            || binding.OperationId is not null
            || binding.ReservationId is not null)
        {
            throw Invalid("A host-scoped owner cannot carry account or workload identity.");
        }
    }

    private static void RequireNoWorkload(NodeResourceOwnerBindingV1 binding)
    {
        if (binding.WorkloadId is not null
            || binding.WorkloadRevision is not null
            || binding.OperationId is not null
            || binding.ReservationId is not null)
        {
            throw Invalid("An account-scoped owner cannot carry workload identity.");
        }
    }

    private static bool IsPositiveInt64(string? value)
        => value is not null
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
            && parsed.ToString(CultureInfo.InvariantCulture) == value;

    private static bool IsLowerSha256(string? value)
        => value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InvalidDataException Invalid(string message) => new(message);
}
