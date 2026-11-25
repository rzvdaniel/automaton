using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Automaton.Api.Mcp;

/// <summary>
/// Read-only view of the caller's apps for the MCP endpoint. Every method goes through the services the CLI's API
/// endpoints use, so the caller sees what the same API key sees there. An app the caller cannot see is "not found".
/// Results carry no secret values, API keys or tokens.
/// </summary>
public sealed class AppMcpReader(
    AppsService apps,
    AppActionExecutionService executions,
    IApplicationEventService events,
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    UserContextService users,
    AppFileStorageService storage,
    Microsoft.Extensions.Options.IOptions<Automaton.Api.Config.PublishedAppsConfig>? publishedApps = null)
{
    public const int MaxLogLines = 500;
    public const int DefaultLogLines = 100;
    private const int MaxLineChars = 2000;
    private const int MaxDocChars = 40_000;
    private const int MaxActivityItems = 20;

    public const string NotFound = "App not found.";

    public async Task<object> ListAppsAsync(CancellationToken token)
        => (await apps.ListAsync(null, token)).Select(app => Summarize(app, publishedApps?.Value)).ToList();

    public async Task<object> StatusAsync(Guid appId, CancellationToken token)
    {
        var app = (await apps.ListAsync(null, token)).FirstOrDefault(item => item.Id == appId) ?? throw new KeyNotFoundException(NotFound);
        return new
        {
            app.Id, app.Name, app.Summary,
            state = app.IsPaused ? "paused" : app.LiveEnvironment?.Status ?? "Unassigned",
            app.IsPaused, app.PausedReason,
            version = app.LiveEnvironment?.LastSuccessfulDeploymentAtUtc,
            lastDeploymentError = app.LiveEnvironment?.LastDeploymentError,
            runner = app.Runner is null ? null : new { app.Runner.Name, online = app.LiveEnvironment?.RunnerOnline },
            address = app.PublicUrl,
            url = AppMcpUrls.For(publishedApps?.Value, app.Id, app.PublicUrl, app.IsPublished),
            whoCanOpen = app.IsPublished ? "anyone with the link (published)" : "the people the app is shared with",
            app.ActionsOnly, app.LastActionRun, app.NextActionRunAtUtc, app.CanUpdate,
        };
    }

    public async Task<object> ActivityAsync(Guid appId, CancellationToken token)
    {
        var app = (await apps.ListAsync(null, token)).FirstOrDefault(item => item.Id == appId) ?? throw new KeyNotFoundException(NotFound);
        var userId = users.GetUserId();
        object runs = Array.Empty<object>();
        string? runsNote = null;
        try
        {
            runs = (await executions.ListAsync(appId, userId, token)).Take(MaxActivityItems).Select(run => new
            {
                run.Id, action = run.ModuleKey, run.Status, run.TriggerType, run.ExitCode,
                run.FailureSummary, run.CreatedAtUtc, run.CompletedAtUtc,
            }).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { runsNote = "Runs are not visible to you."; }
        var recent = await events.GetEventsAsync(appId, new ApplicationEventQuery { Page = 1, PageSize = MaxActivityItems }, token);
        return new
        {
            runs, runsNote,
            events = recent.Items.Select(e => new { e.Level, e.Category, e.EventType, e.Message, e.CreatedAtUtc }).ToList(),
            lastDeploymentError = app.LiveEnvironment?.LastDeploymentError,
            lastSuccessfulDeploymentAtUtc = app.LiveEnvironment?.LastSuccessfulDeploymentAtUtc,
        };
    }

    public async Task<object> LogsAsync(Guid appId, Guid? runId, int? lines, CancellationToken token)
    {
        _ = (await apps.ListAsync(null, token)).FirstOrDefault(item => item.Id == appId) ?? throw new KeyNotFoundException(NotFound);
        var count = Math.Clamp(lines ?? DefaultLogLines, 1, MaxLogLines);
        var userId = users.GetUserId();
        try
        {
            var id = runId ?? (await executions.ListAsync(appId, userId, token)).FirstOrDefault()?.Id;
            if (id is null) return new { available = false, message = "This app has no runs with logs yet." };
            var artifact = await executions.ReadArtifactAsync(appId, id.Value, userId, result: false, token);
            if (!artifact.Available || artifact.Content is null) return new { runId = id, available = false, message = artifact.Message ?? "Logs are not available." };
            var tail = artifact.Content.Split('\n').TakeLast(count).Select(line => line.Length > MaxLineChars ? line[..MaxLineChars] : line);
            return new { runId = id, available = true, lines = tail.ToList() };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { throw new KeyNotFoundException(NotFound); }
    }

    public async Task<object> DocsAsync(Guid appId, CancellationToken token)
    {
        var userId = users.GetUserId();
        // Same rule as the source browser: the people who manage the app and administrators.
        if (!await authorization.CanUpdateAppAsync(appId, userId, token) && !await authorization.IsAdministratorAsync(userId, token))
            throw new KeyNotFoundException(NotFound);
        if (!await db.WebApps.AnyAsync(app => app.Id == appId && app.DeletionClaimId == null, token))
            throw new KeyNotFoundException(NotFound);
        var owner = await db.WebAppUsers.Where(link => link.WebAppId == appId).OrderBy(link => link.UserId)
            .Select(link => (Guid?)link.UserId).FirstOrDefaultAsync(token) ?? throw new KeyNotFoundException(NotFound);
        var bytes = await storage.ReadSourceFileBytesAsync(owner, appId, "README.md");
        if (bytes is null) return new { available = false, message = "This app has no README.md." };
        var text = Encoding.UTF8.GetString(bytes);
        return new { available = true, readme = text.Length > MaxDocChars ? text[..MaxDocChars] : text };
    }

    private static object Summarize(AppListItemDto app, Automaton.Api.Config.PublishedAppsConfig? config) => new
    {
        app.Id, app.Name, app.Summary,
        state = app.IsPaused ? "paused" : app.LiveEnvironment?.Status ?? "Unassigned",
        address = app.PublicUrl, url = AppMcpUrls.For(config, app.Id, app.PublicUrl, app.IsPublished), app.IsPublished, app.UpdatedAtUtc,
    };
}
