using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Automaton.Api.Mcp;

/// <summary>
/// The MCP tools that change things: create, deploy, share, pause, resume, install from the catalog. Each call goes
/// through the same services the CLI's API endpoints use, so the caller's rights are exactly the API key's. An app the
/// caller cannot manage is "not found". Results carry no secret values, API keys or tokens.
/// </summary>
public sealed class AppMcpManager(
    AppsService apps,
    DeploymentOperationService deployments,
    AppSharingService sharing,
    AppPauseService pause,
    AppSecretService secrets,
    AppCatalogService catalog,
    AppCatalogInstallService installer,
    IAiGuidanceRuntimeService guidance,
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    UserContextService users,
    ConfirmedEmailGuardService confirmedEmail,
    IHttpContextAccessor http,
    RunnerService runners,
    ConfigurationService configuration)
{
    public const long MaxDeployBytes = AppArchiveStorageBounds.MaximumUploadBytes;
    public const long MaxRequestBytes = MaxDeployBytes * 2 + 1024 * 1024;
    public const int MaxFiles = 2000;
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(120);
    private const int FailureEvents = 10;

    public TimeSpan Wait { get; set; } = DefaultWait;

    public async Task<object> CreateAppAsync(string name, string? summary, CancellationToken token)
    {
        await RequireConfirmedEmailAsync(token);
        if (string.IsNullOrWhiteSpace(name)) throw new McpUserException("Give the app a name.");
        try
        {
            var id = await apps.Create(new NewApp { Name = name.Trim() });
            return new { appId = id, name = name.Trim(), studio = StudioLink(id), next = "Call deploy_app with the app's files, including automaton.json." };
        }
        catch (InvalidOperationException ex) { throw new McpUserException(ex.Message); }
    }

    public async Task<object> DeployAppAsync(Guid appId, IReadOnlyDictionary<string, string> files, CancellationToken token)
    {
        var userId = users.GetUserId();
        if (!await authorization.CanUpdateAppAsync(appId, userId, token)) throw new McpUserException(AppMcpReader.NotFound);
        await RequireConfirmedEmailAsync(token);
        ValidateFiles(files);
        await EnsureRunnerAsync(appId, token);

        var directory = Path.Combine(Path.GetTempPath(), "automaton-mcp-" + Guid.NewGuid().ToString("N"));
        DeploymentOperationResponse accepted;
        try
        {
            foreach (var (path, content) in files)
            {
                var full = Path.GetFullPath(Path.Combine(directory, path));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, content, new UTF8Encoding(false), token);
            }
            await using var package = StoredSourceDeploymentService.CreatePackage(appId, directory);
            var file = new FormFile(package, 0, package.Length, "file", "deploy-package.zip");
            accepted = await deployments.AcceptAsync(appId, userId, null, file, clean: false, token);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            throw new McpUserException("The app was not accepted: " + ex.Message);
        }
        catch (KeyNotFoundException) { throw new McpUserException(AppMcpReader.NotFound); }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }

        return await WaitForOperationAsync(appId, userId, accepted.OperationId, token);
    }

    public async Task<object> ShareAppAsync(Guid appId, string audience, string? publicUrl, CancellationToken token)
    {
        await RequireConfirmedEmailAsync(token);
        try
        {
            var state = await sharing.SetAudienceAsync(appId, audience, publicUrl, null, null, token);
            return new { appId, audience = state.Audience, publicUrl = state.PublicUrlSlug, people = state.People.Select(p => p.Email).ToList() };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException) { throw new McpUserException(AppMcpReader.NotFound); }
        catch (InvalidOperationException ex) { throw new McpUserException(ex.Message); }
    }

    public async Task<object> PauseAsync(Guid appId, bool pauseIt, CancellationToken token)
    {
        await RequireConfirmedEmailAsync(token);
        try
        {
            var admin = await authorization.IsAdministratorAsync(users.GetUserId(), token);
            var state = pauseIt ? await pause.PauseAsync(appId, admin, CancellationToken.None) : await pause.ResumeAsync(appId, admin, CancellationToken.None);
            return new { appId, state };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException) { throw new McpUserException(AppMcpReader.NotFound); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or RuntimeCommandFailedException) { throw new McpUserException(ex.Message); }
    }

    public async Task<object> MissingSecretsAsync(Guid appId, CancellationToken token)
    {
        var userId = users.GetUserId();
        if (!await authorization.CanUpdateAppAsync(appId, userId, token)) throw new McpUserException(AppMcpReader.NotFound);
        var json = await db.WebApps.Where(a => a.Id == appId).Select(a => a.ApplicationContractJson).FirstOrDefaultAsync(token);
        var declarations = string.IsNullOrWhiteSpace(json) ? [] : ApplicationContractSerialization.Deserialize(json).Secrets;
        var status = declarations.Count == 0 ? [] : await secrets.StatusAsync(appId, userId, declarations, token);
        var missing = status.Where(s => !s.Present && !s.Optional).Select(s => new { s.Name, s.Description }).ToList();
        return new
        {
            missing,
            studio = missing.Count == 0 ? null : StudioLink(appId),
            note = missing.Count == 0 ? "No required secret is missing." : "Open the Studio link and fill these in. Secret values never pass through MCP.",
        };
    }

    public async Task<object> ListCatalogAsync(string? query, CancellationToken token)
    {
        var result = await catalog.GetAsync(users.GetUserId(), false, token, new CatalogQuery { Q = query, PageSize = 30 });
        return new { result.Apps, result.Total, result.Unavailable, result.Message };
    }

    public async Task<object> InstallFromCatalogAsync(string repo, string? folder, string commit, string? name, CancellationToken token)
    {
        await RequireConfirmedEmailAsync(token);
        try
        {
            var app = await installer.InstallAsync(new CatalogInstallRequest { Repo = repo, Folder = folder, Commit = commit, Name = name }, token);
            return new { appId = app.Id, name = app.Name, studio = StudioLink(app.Id), next = "Use app_status to follow it, and missing_secrets to see what it still needs." };
        }
        catch (ArgumentException ex) { throw new McpUserException(ex.Message); }
    }

    public async Task<object> GuidanceAsync(CancellationToken token)
    {
        var pack = await guidance.BuildExportPackAsync(token);
        return pack.Files.Select(f => new { path = f.TargetPath, content = f.Content }).ToList();
    }

    public static void ValidateFiles(IReadOnlyDictionary<string, string> files, long maxBytes = MaxDeployBytes)
    {
        if (files is null || files.Count == 0) throw new McpUserException("Send the app's files, including automaton.json.");
        if (files.Count > MaxFiles) throw new McpUserException($"At most {MaxFiles} files per deploy.");
        long total = 0;
        foreach (var (path, content) in files)
        {
            var normalized = path.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path) || normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(part => part is ".." or ""))
                throw new McpUserException($"Not a relative file path: {path}");
            total += Encoding.UTF8.GetByteCount(content ?? string.Empty);
        }
        if (total > maxBytes) throw new McpUserException($"The files are {total / 1024 / 1024} MB; the limit is {maxBytes / 1024 / 1024} MB, the same as the CLI upload.");
        if (!files.Keys.Any(p => p.Replace('\\', '/') == "automaton.json")) throw new McpUserException("automaton.json is missing. Call app_guidance for the format.");
    }

    private async Task<object> WaitForOperationAsync(Guid appId, Guid userId, Guid operationId, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + Wait;
        DeploymentOperationResponse? operation = null;
        while (DateTime.UtcNow < deadline)
        {
            operation = await deployments.GetAsync(appId, userId, operationId, token);
            if (operation?.Status is "Succeeded" or "Failed") break;
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        var app = (await apps.ListAsync(null, token)).FirstOrDefault(item => item.Id == appId);
        var state = AppState(app);
        if (operation?.Status == "Succeeded" && operation.Result?.Success != false && state == "Running")
            return new { appId, deployed = true, state, address = app?.PublicUrl, studio = StudioLink(appId) };
        return await DescribeFailureAsync(appId, operation, app, state, token);
    }

    private static string AppState(AppListItemDto? app)
        => app is null ? "Unknown" : app.IsPaused ? "paused" : app.LiveEnvironment?.Status ?? "Unassigned";

    private async Task<object> DescribeFailureAsync(Guid appId, DeploymentOperationResponse? operation, AppListItemDto? app, string state, CancellationToken token)
    {
        var failed = operation?.Status == "Failed" || operation?.Result?.Success == false;
        return new
        {
            appId, deployed = false, state,
            status = operation?.Status ?? "Unknown",
            timedOut = !failed && operation?.Status is not "Succeeded",
            error = operation?.Error ?? app?.LiveEnvironment?.LastDeploymentError,
            logTail = await EventTailAsync(appId, token),
            note = failed ? "The deploy failed. Fix the files and call deploy_app again." : "Not Running yet. Call app_status or app_logs to follow it.",
        };
    }

    private async Task<List<string>> EventTailAsync(Guid appId, CancellationToken token)
        => (await db.ApplicationEvents.Where(e => e.AppId == appId).OrderByDescending(e => e.CreatedAtUtc).Take(FailureEvents)
            .Select(e => e.Message).ToListAsync(token)).Select(m => m.Length > 500 ? m[..500] : m).Reverse().ToList();

    private async Task RequireConfirmedEmailAsync(CancellationToken token)
    {
        if (!await confirmedEmail.IsConfirmedAsync(users.GetUserId()))
            throw new McpUserException(ConfirmedEmailGuardService.SensitiveActionMessage);
    }

    /// <summary>
    /// An app with no Runner gets the only online Management Runner visible to it, as `automaton deploy` does. With none
    /// or several, the caller is told what to do instead of a bare refusal.
    /// </summary>
    private async Task EnsureRunnerAsync(Guid appId, CancellationToken token)
    {
        var app = await db.WebApps.Include(a => a.DeploymentTargets).AsNoTracking().FirstOrDefaultAsync(a => a.Id == appId, token);
        if (app is null) throw new McpUserException(AppMcpReader.NotFound);
        if (app.DeploymentTargets?.SingleOrDefault(t => t.IsDefault)?.ExecutionRunnerId is not null) return;
        var chosen = SelectOnlyAvailableRunner(await runners.List(app.ProjectId, token), StudioLink(appId));
        try { await apps.UpdateRunner(appId, chosen); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { throw new McpUserException(ex.Message); }
    }

    internal static Guid SelectOnlyAvailableRunner(IEnumerable<RunnerDetails> candidates, string? studio)
    {
        var available = candidates.Where(r => r.Status == RunnerStatus.Online && !r.IsHostedRunner && r.CanExecuteApplications).ToList();
        var where = studio is null ? "in Studio" : $"in Studio ({studio})";
        return available.Count switch
        {
            1 => available[0].Id,
            0 => throw new McpUserException($"This app has no Runner and no online Management Runner is available to it. Install or start one under Runners {where}, then call deploy_app again."),
            _ => throw new McpUserException("This app has no Runner and several online Runners are available: " + string.Join(", ", available.Select(r => r.Name)) + $". Choose one {where} on the app's page, then call deploy_app again."),
        };
    }

    private string? StudioLink(Guid appId) => StudioLink(configuration.WebsiteUrl, appId);

    internal static string? StudioLink(string? websiteUrl, Guid appId)
        => Uri.TryCreate(websiteUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? $"{uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)}/apps/{appId}"
            : null;
}

/// <summary>A refusal the caller should read: the message is safe to return to the AI.</summary>
public sealed class McpUserException(string message) : Exception(message);
