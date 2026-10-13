namespace Automaton.Api.Entities;

/// <summary>Durable control-plane activation state for one enrolled Node identity.</summary>
public sealed class NodeManagement
{
    public Guid NodeId { get; set; }
    public Guid InstallationId { get; set; }
    public Guid EnrollmentAuthorizationId { get; set; }
    public Guid ControlPlaneRealmId { get; set; }
    public long CredentialRevision { get; set; }
    public long RecoveryEpoch { get; set; }
    public long? CurrentIncarnation { get; set; }
    public string Status { get; set; } = NodeManagementStatuses.Enrolled;
    public Guid? CurrentActivationRequestId { get; set; }
    public long HostFenceGeneration { get; set; }
    public string? RecoveryCheckpointSha256 { get; set; }
    public ICollection<NodeActivationAttempt> ActivationAttempts { get; set; } = new List<NodeActivationAttempt>();
}

public static class NodeManagementStatuses
{
    public const string Enrolled = "Enrolled";
    public const string Recovering = "Recovering";
    public const string Quiescing = "Quiescing";
    public const string Fenced = "Fenced";
    public const string Revoked = "Revoked";
}

/// <summary>Immutable result of one activation request, including retries after a lost response.</summary>
public sealed class NodeActivationAttempt
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public Guid InstallationId { get; set; }
    public Guid ActivationRequestId { get; set; }
    public string RequestSha256 { get; set; } = string.Empty;
    public long? ExpectedIncarnation { get; set; }
    public long AssignedIncarnation { get; set; }
    public Guid ControlPlaneRealmId { get; set; }
    public long RecoveryEpoch { get; set; }
    public long CredentialRevision { get; set; }
    public string Status { get; set; } = NodeManagementStatuses.Recovering;
    public byte[] ImmutableReceiptJson { get; set; } = Array.Empty<byte>();
    public Guid? HostFenceReceiptId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public NodeManagement NodeManagement { get; set; } = null!;
}

/// <summary>Unique consumption record for an HFA host-fence receipt.</summary>
public sealed class NodeHostFenceReceiptUse
{
    public Guid ReceiptId { get; set; }
    public Guid NodeId { get; set; }
    public Guid ActivationRequestId { get; set; }
    public long HostFenceGeneration { get; set; }
    public DateTime ConsumedAtUtc { get; set; }
}
