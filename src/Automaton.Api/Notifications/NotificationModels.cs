namespace Automaton.Api.Notifications;

/// <summary>One notice for the signed-in person. Repeats of a kind are grouped into one with a <c>Count</c>.</summary>
/// <param name="Severity">info, warning or critical. Critical notices cannot be acknowledged; they go when the cause is fixed.</param>
/// <param name="ActionPath">A Studio route.</param>
public sealed record NotificationItem(
    string Key, string Kind, string Severity, string Title, string Detail, string ActionLabel, string ActionPath,
    int Count, IReadOnlyList<string> Subjects, bool Acknowledgeable);

public sealed record NotificationListResponse(IReadOnlyList<NotificationItem> Notifications);

public static class NotificationSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}

public static class NotificationKinds
{
    public const string ServerUpdate = "server-update";
    public const string PermissionRequests = "permission-requests";
    public const string AppFailed = "app-failed";
    public const string RunnerOffline = "runner-offline";
    public const string CertificateExpiring = "certificate-expiring";
    public const string BackupFailed = "backup-failed";

    /// <summary>Kinds that live in the feed, so Home does not repeat them as Needs-you cards.</summary>
    public static readonly IReadOnlySet<string> InFeed = new HashSet<string>(StringComparer.Ordinal)
    {
        ServerUpdate, PermissionRequests, AppFailed,
    };
}
