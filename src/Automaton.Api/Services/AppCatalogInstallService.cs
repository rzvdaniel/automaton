using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Api.Services;

/// <summary>
/// Installs an app from a public GitHub repository at one pinned commit: a catalog entry or a pasted Git URL.
/// The files are fetched on the server (never on the browser), only from github.com, and written as the installing
/// user's own app; permissions are not granted here.
/// </summary>
public sealed partial class AppCatalogInstallService(
    ApplicationDbContext dbContext,
    UserContextService userContextService,
    AppFileStorageService fileStorageService,
    AppRunnerAssignmentPolicy runnerAssignmentPolicy,
    RecentActivityService recentActivityService,
    Services.Interfaces.IAuditEventService auditEventService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    private Guid UserId => userContextService.GetUserId();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex RepoPattern();

    [GeneratedRegex(@"^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"^https://github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?(?:/tree/([A-Za-z0-9_.-]+)(?:/(.+?))?)?/?$")]
    private static partial Regex GitUrlPattern();

    public static bool IsValidSource(string repo, string? folder, string commit) =>
        RepoPattern().IsMatch(repo)
        && CommitPattern().IsMatch(commit)
        && IsValidFolder(folder);

    private static bool IsValidFolder(string? folder) =>
        string.IsNullOrEmpty(folder) || (folder.Length <= 300 && FolderPattern().IsMatch(folder) && !folder.Split('/').Any(part => part is "." or ".."));

    /// <summary>Turns a pasted https://github.com/owner/repo[/tree/ref[/folder]] URL into a pinned source.</summary>
    public async Task<GitAppSource> ResolveUrlAsync(string url, CancellationToken cancellationToken)
    {
        var match = GitUrlPattern().Match((url ?? "").Trim());
        if (!match.Success)
            throw new ArgumentException("Paste a public GitHub repository address such as https://github.com/owner/name.");
        var repo = $"{match.Groups[1].Value}/{match.Groups[2].Value}";
        var folder = match.Groups[4].Success ? match.Groups[4].Value : "";
        if (!IsValidFolder(folder)) throw new ArgumentException("That folder name is not valid.");
        var reference = match.Groups[3].Success ? match.Groups[3].Value : "HEAD";

        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base("Catalog:ApiBaseUrl", "https://api.github.com")}/repos/{repo}/commits/{Uri.EscapeDataString(reference)}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github.sha");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ArgumentException("GitHub could not find that repository or branch. Only public repositories can be installed.");
        var commit = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!CommitPattern().IsMatch(commit)) throw new ArgumentException("GitHub did not return a commit for that address.");
        return new GitAppSource(repo, folder, commit);
    }

    public async Task<CatalogInstallSourceModel> GetSourceAsync(GitAppSource source, CancellationToken cancellationToken)
    {
        using var package = await DownloadAsync(source, cancellationToken);
        var manifest = ReadManifest(package, source);
        return new CatalogInstallSourceModel
        {
            Name = manifest.Name,
            Summary = manifest.Summary,
            Description = manifest.Description,
            Repo = source.Repo,
            Folder = source.Folder,
            Commit = source.Commit,
            Application = manifest.Application
        };
    }

    public async Task<AppModel> InstallAsync(CatalogInstallRequest request, CancellationToken cancellationToken)
    {
        var source = Validated(request.Repo, request.Folder, request.Commit);
        using var package = await DownloadAsync(source, cancellationToken);
        var manifest = ReadManifest(package, source);
        var name = string.IsNullOrWhiteSpace(request.Name) ? manifest.Name : request.Name.Trim();
        if (name.Length > 200) name = name[..200];

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var entity = new WebApp
        {
            Id = id,
            Name = name,
            Summary = manifest.Summary,
            Description = manifest.Description,
            Definition = "{}",
            ApplicationContractJson = ApplicationContractSerialization.Serialize(manifest.Application),
            Version = 1,
            Scope = AccessScope.Personal,
            Tags = AppTagNormalizer.Serialize((string?)null),
            InstalledFromRepo = source.Repo,
            InstalledFromFolder = source.Folder,
            InstalledFromCommit = source.Commit,
            Created = now,
            Updated = now
        };
        dbContext.WebApps.Add(entity);
        dbContext.WebAppUsers.Add(new WebAppUser { WebAppId = id, UserId = UserId });
        if (request.RunnerId.HasValue)
        {
            try { await runnerAssignmentPolicy.AssignAsync(entity, request.RunnerId.Value, UserId); }
            catch (InvalidOperationException exception) { throw new ArgumentException(exception.Message, nameof(request), exception); }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await WriteSourceAsync(package, source, id, cancellationToken);
        }
        catch
        {
            // Do not leave an app without its files behind (it would also show as Installed in the catalog).
            dbContext.WebApps.Remove(entity);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            var directory = fileStorageService.GetAppSourceDirectoryPath(UserId, id);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            throw;
        }
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, entity.Name, id);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.InstalledFromSource,
            EntityType = AuditEntityTypes.App,
            EntityId = id,
            EventType = AuditEventTypes.AppInstalledFromSource,
            Message = $"App '{entity.Name}' was installed from {source.Repo}",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new { source.Repo, source.Folder, source.Commit, NewAppId = id }
        });

        var saved = dbContext.WebApps.Local.First(app => app.Id == id);
        var model = AppsService.MapToModel(saved, null, UserId);
        AppsService.ApplyStorageMetadata(model, await fileStorageService.GetAppStorageMetadataAsync(UserId, id));
        return model;
    }

    private static GitAppSource Validated(string repo, string? folder, string commit)
    {
        folder = (folder ?? "").Trim('/');
        if (!IsValidSource(repo ?? "", folder, commit ?? ""))
            throw new ArgumentException("The source must be a public GitHub owner/name, an optional folder and a full 40-character commit.");
        return new GitAppSource(repo!, folder, commit!);
    }

    public static GitAppSource Validate(string repo, string? folder, string commit) => Validated(repo, folder, commit);

    private async Task<ZipArchive> DownloadAsync(GitAppSource source, CancellationToken cancellationToken)
    {
        using var client = Client();
        using var response = await client.GetAsync(
            $"{Base("Catalog:ArchiveBaseUrl", "https://codeload.github.com")}/{source.Repo}/zip/{source.Commit}",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ArgumentException("GitHub could not provide that commit. Only public repositories can be installed.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > AppArchiveStorageBounds.MaximumUploadBytes)
                throw new ArgumentException("That repository is too large to install.");
        }
        buffer.Position = 0;
        try
        {
            var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            AppArchiveStorageBounds.Validate(archive);
            return archive;
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("GitHub returned an archive that cannot be read: " + exception.Message);
        }
    }

    /// <summary>The files of the app, with the archive's top-level folder and the app folder removed.</summary>
    private static IEnumerable<(ZipArchiveEntry Entry, string Path)> AppFiles(ZipArchive package, GitAppSource source)
    {
        var prefix = source.Folder.Length == 0 ? "" : source.Folder + "/";
        foreach (var entry in package.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var full = entry.FullName.Replace('\\', '/');
            var slash = full.IndexOf('/');
            if (slash < 0) continue;
            var inside = full[(slash + 1)..];
            if (!inside.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var relative = inside[prefix.Length..];
            if (relative.Length > 0) yield return (entry, relative);
        }
    }

    private static Manifest ReadManifest(ZipArchive package, GitAppSource source)
    {
        var file = AppFiles(package, source).FirstOrDefault(item => item.Path == "automaton.json");
        if (file.Entry is null)
            throw new ArgumentException("That location has no automaton.json, so it is not an Automaton app.");
        try
        {
            using var reader = new StreamReader(file.Entry.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            var contract = root.TryGetProperty("application", out var application)
                ? ApplicationContractSerialization.Deserialize(application.GetRawText())
                : ApplicationContractDefaults.Create();
            string? Text(string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var name = Text("name") ?? source.Folder.Split('/').Last();
            if (string.IsNullOrWhiteSpace(name)) name = source.Repo.Split('/').Last();
            return new Manifest(name, Text("summary"), Text("description"), contract);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ArgumentException("The app's automaton.json is not valid: " + exception.Message);
        }
    }

    private async Task WriteSourceAsync(ZipArchive package, GitAppSource source, Guid appId, CancellationToken cancellationToken)
    {
        await fileStorageService.SaveAppAsync(UserId, appId, new AppDefinition { HtmlContent = null, Version = 1 });
        await fileStorageService.UpdateAppStorageMetadataAsync(UserId, appId, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Studio;
            metadata.SourceAvailable = true;
            metadata.RuntimeSource = AppRuntimeSources.Studio;
        });

        var root = Path.GetFullPath(fileStorageService.GetAppSourceDirectoryPath(UserId, appId));
        foreach (var (entry, relative) in AppFiles(package, source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException($"The archive entry '{relative}' is outside the app folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await AppArchiveStorageBounds.CopyExactlyAsync(input, output, entry.Length, AppArchiveStorageBounds.MaximumExpandedBytes, cancellationToken);
        }
    }

    private HttpClient Client()
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Automaton-Server");
        return client;
    }

    private string Base(string key, string fallback) => (configuration[key] is { Length: > 0 } value ? value : fallback).TrimEnd('/');

    private sealed record Manifest(string Name, string? Summary, string? Description, ApplicationContract Application);
}
