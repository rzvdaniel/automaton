using Automaton.Core.Models;
using Automaton.Api.Models;

namespace Automaton.Api.Home;

/// <summary>One thing on Studio Home that needs the signed-in person. <c>ActionPath</c> is a Studio route.</summary>
public sealed record HomeCard(
    string Key, string Kind, string Severity, string Title, string Detail,
    string ActionLabel, string ActionPath, bool Dismissible, Guid? AppId = null, string? CatalogAppId = null);

public sealed record HomePinnedApp(AppListItemDto App, DateTime PinnedAtUtc, AppHomeCardDto? Card = null);

public sealed record HomeResponse(IReadOnlyList<HomeCard> NeedsYou, IReadOnlyList<HomePinnedApp> Pinned);

public sealed record HomePinOrderRequest(IReadOnlyList<Guid> AppIds);

public static class HomeCardKeys
{
    public const string NoRunner = "no-runner";
    public const string NoSmtp = "no-smtp";
    public const string NoBackup = "no-backup";
    public const string PermissionRequests = "permission-requests";
    public const string ServerUpdate = "server-update";
    public const string RunnerUpdate = "runner-update";
    public const string AppFailed = "app-failed";
    public const string InstalledForYou = "installed-for-you";
    public const string RecommendedApp = "recommended-app";
    public const string RecommendedPrefix = "recommended:";

    /// <summary>Only optional cards can be dismissed; the others go away when their task is done.</summary>
    public static readonly IReadOnlySet<string> Dismissible = new HashSet<string>(StringComparer.Ordinal) { NoSmtp, NoBackup, InstalledForYou };

    public static bool CanDismiss(string key) => Dismissible.Contains(key) || key.StartsWith(RecommendedPrefix, StringComparison.Ordinal) && key.Length is > 12 and <= 100;
}
