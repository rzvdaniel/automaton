namespace Automaton.Api.Constants;

/// <summary>
/// Generic audit verbs. Use these for the broad action category.
/// Pair with a specific business event in <see cref="AuditEventTypes"/>.
/// Example: EntityType=Runner, Action=Assigned, EventType=RunnerAssignedToApp.
/// </summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Deleted = "Deleted";
    public const string Updated = "Updated";
    public const string Renamed = "Renamed";
    public const string Changed = "Changed";
    public const string Disabled = "Disabled";
    public const string Enabled = "Enabled";
    public const string Requested = "Requested";
    public const string Started = "Started";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Added = "Added";
    public const string Removed = "Removed";
    public const string Published = "Published";
    public const string Unpublished = "Unpublished";
    public const string Forked = "Forked";
    public const string Assigned = "Assigned";
    public const string Unassigned = "Unassigned";
    public const string Delivered = "Delivered";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Revoked = "Revoked";
}
