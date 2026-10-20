using System.Text;
using System.Text.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;

namespace Automaton.Runner.Console.Services;

public sealed class MachineStaticSitePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<MachineStaticSitePublisher>();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly RunnerWorkspaceService workspace;
    private readonly MachineStaticServingOptions settings;
    private readonly Func<HostContainerScope, CancellationToken, Task<MachineStaticServerEndpoint?>> ensureContainer;
    private readonly Func<HostContainerScope, CancellationToken, Task<bool>> reloadContainer;

    public MachineStaticSitePublisher(
        InfrastructureCommandService docker,
        IOptions<MachineStaticServingOptions> options)
    {
        workspace = new RunnerWorkspaceService();
        settings = options.Value;
        var containers = new MachineStaticServingContainer(docker, options.Value);
        ensureContainer = containers.EnsureAsync;
        reloadContainer = containers.ReloadAsync;
    }

    internal MachineStaticSitePublisher(
        RunnerWorkspaceService workspace,
        MachineStaticServingOptions settings,
        Func<HostContainerScope, CancellationToken, Task<MachineStaticServerEndpoint?>> ensureContainer,
        Func<HostContainerScope, CancellationToken, Task<bool>> reloadContainer)
    {
        this.workspace = workspace;
        this.settings = settings;
        this.ensureContainer = ensureContainer;
        this.reloadContainer = reloadContainer;
    }

    public async Task<MachineStaticServerEndpoint?> PublishAsync(
        Guid operationId,
        Guid appId,
        Guid targetId,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try { return await PublishCoreAsync(operationId, appId, targetId, outputDirectory, cancellationToken); }
        finally { operationGate.Release(); }
    }

    private async Task<MachineStaticServerEndpoint?> PublishCoreAsync(
        Guid operationId,
        Guid appId,
        Guid targetId,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var scope = CreateScope(appId, targetId);
        if (!ValidIdentity(operationId, scope) || !settings.IsValid()) return null;

        var index = ReadIndex();
        if (IsSamePublication(index, appId, operationId))
            return await EnsureExistingPublicationAsync(index, scope, cancellationToken);

        var sourceRoot = ResolveOutputDirectory(appId, targetId, outputDirectory);
        var files = EnumerateSafeFiles(sourceRoot, cancellationToken);
        EnsureIndexFileExists(files);

        return await StageAndPublishAsync(index, appId, operationId, scope, files, cancellationToken);
    }

    private async Task<MachineStaticServerEndpoint?> EnsureExistingPublicationAsync(
        IReadOnlyDictionary<Guid, Guid> index,
        HostContainerScope scope,
        CancellationToken cancellationToken)
    {
        await WriteConfigurationAsync(index, cancellationToken);
        var endpoint = await ensureContainer(scope, cancellationToken);
        return endpoint is not null && await reloadContainer(scope, cancellationToken)
            ? WithSitePath(endpoint, scope.AppId, index[scope.AppId])
            : null;
    }

    private async Task<MachineStaticServerEndpoint?> StageAndPublishAsync(
        Dictionary<Guid, Guid> index,
        Guid appId,
        Guid operationId,
        HostContainerScope scope,
        IReadOnlyCollection<StaticFile> files,
        CancellationToken cancellationToken)
    {
        var sitesRoot = workspace.GetPublishedStaticSitesRoot();
        var stageRoot = Path.Combine(workspace.GetPublishedStaticRoot(), "staging", appId.ToString("D"), operationId.ToString("D"));
        var releaseRoot = Path.Combine(workspace.GetPublishedStaticSiteVersionsRoot(appId), operationId.ToString("D"));
        var previous = index.GetValueOrDefault(appId);
        Directory.CreateDirectory(sitesRoot);
        DeleteTreeWithoutFollowingLinks(stageRoot);
        Directory.CreateDirectory(stageRoot);
        var configurationUpdateStarted = false;
        try
        {
            CopySafeFiles(files, stageRoot, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(releaseRoot)!);
            if (Directory.Exists(releaseRoot)) DeleteTreeWithoutFollowingLinks(releaseRoot);
            Directory.Move(stageRoot, releaseRoot);

            var updated = new Dictionary<Guid, Guid>(index) { [appId] = operationId };
            configurationUpdateStarted = true;
            var endpoint = await TryActivatePublicationAsync(updated, index, scope, cancellationToken);
            if (endpoint is null)
            {
                DeleteTreeWithoutFollowingLinks(releaseRoot);
                return null;
            }

            CleanupOlderReleasesSafely(appId, operationId, previous);
            return WithSitePath(endpoint, appId, operationId);
        }
        catch
        {
            await RollBackFailedPublishAsync(index, appId, operationId, scope, stageRoot, releaseRoot,
                configurationUpdateStarted);
            throw;
        }
    }

    private static bool IsSamePublication(IReadOnlyDictionary<Guid, Guid> index, Guid appId, Guid operationId)
        => index.TryGetValue(appId, out var current) && current == operationId;

    private static void EnsureIndexFileExists(IReadOnlyCollection<StaticFile> files)
    {
        if (files.Count == 0 || files.All(file => !string.Equals(file.RelativePath, "index.html", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Static publishing requires index.html in the declared build output directory.");
    }

    private void CleanupOlderReleasesSafely(Guid appId, Guid operationId, Guid? previous)
    {
        try { CleanupOldReleases(appId, operationId, previous); }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not remove an older static-site release for app {AppId}", appId);
        }
    }

    private async Task RollBackFailedPublishAsync(
        IReadOnlyDictionary<Guid, Guid> previousSites,
        Guid appId,
        Guid operationId,
        HostContainerScope scope,
        string stageRoot,
        string releaseRoot,
        bool configurationUpdateStarted)
    {
        DeleteTreeWithoutFollowingLinks(stageRoot);
        if (configurationUpdateStarted)
        {
            try { await RestoreConfigurationAsync(previousSites, scope, CancellationToken.None); }
            catch (Exception exception)
            {
                Logger.Error(exception, "Could not restore the previous static-site configuration for app {AppId}", appId);
            }
        }
        if (!previousSites.TryGetValue(appId, out var previous) || previous != operationId)
            DeleteTreeWithoutFollowingLinks(releaseRoot);
    }

    public async Task<bool> UnpublishAsync(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try { return await UnpublishCoreAsync(appId, targetId, cancellationToken); }
        finally { operationGate.Release(); }
    }

    private async Task<bool> UnpublishCoreAsync(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var scope = CreateScope(appId, targetId);
        if (!ValidIdentity(Guid.NewGuid(), scope) || !settings.IsValid()) return false;
        var index = ReadIndex();
        if (!index.ContainsKey(appId))
        {
            DeleteTreeWithoutFollowingLinks(workspace.GetPublishedStaticSiteVersionsRoot(appId));
            return true;
        }

        var updated = new Dictionary<Guid, Guid>(index);
        updated.Remove(appId);
        var unpublishCommitted = false;
        try
        {
            await WriteConfigurationAsync(updated, cancellationToken);
            var endpoint = await ensureContainer(scope, cancellationToken);
            if (endpoint is null || !await reloadContainer(scope, cancellationToken))
            {
                await RestoreConfigurationAsync(index, scope, CancellationToken.None);
                return false;
            }

            unpublishCommitted = true;
            DeleteTreeWithoutFollowingLinks(workspace.GetPublishedStaticSiteVersionsRoot(appId));
            return true;
        }
        catch
        {
            if (!unpublishCommitted)
            {
                try { await RestoreConfigurationAsync(index, scope, CancellationToken.None); }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Could not restore the prior static-site configuration after unpublish failure for app {AppId}", appId);
                }
            }
            throw;
        }
    }

    public async Task<MachineStaticServerEndpoint?> GetEndpointAsync(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try { return await GetEndpointCoreAsync(appId, targetId, cancellationToken); }
        finally { operationGate.Release(); }
    }

    private async Task<MachineStaticServerEndpoint?> GetEndpointCoreAsync(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var scope = CreateScope(appId, targetId);
        var index = ReadIndex();
        if (!ValidIdentity(Guid.NewGuid(), scope) || !settings.IsValid() || !index.TryGetValue(appId, out var releaseId))
            return null;
        var endpoint = await ensureContainer(scope, cancellationToken);
        return endpoint is null ? null : WithSitePath(endpoint, appId, releaseId);
    }

    internal static string BuildNginxConfiguration(IReadOnlyDictionary<Guid, Guid> sites, int port)
    {
        var builder = new StringBuilder();
        builder.AppendLine("server {");
        builder.AppendLine($"    listen {port};");
        builder.AppendLine("    server_name _;");
        builder.AppendLine("    autoindex off;");
        builder.AppendLine("    location = /healthz { access_log off; return 204; }");
        foreach (var (appId, releaseId) in sites.OrderBy(pair => pair.Key))
        {
            var app = appId.ToString("D");
            var release = releaseId.ToString("D");
            builder.AppendLine($"    location ^~ /{app}/{release}/ {{");
            builder.AppendLine("        root /sites;");
            builder.AppendLine("        index index.html;");
            builder.AppendLine($"        try_files $uri $uri/ /{app}/{release}/index.html;");
            builder.AppendLine("    }");
        }
        builder.AppendLine("    location / { return 404; }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static MachineStaticServerEndpoint WithSitePath(
        MachineStaticServerEndpoint endpoint,
        Guid appId,
        Guid releaseId)
        => endpoint with { PathPrefix = $"/{appId:D}/{releaseId:D}/" };

    private async Task WriteConfigurationAsync(
        IReadOnlyDictionary<Guid, Guid> sites,
        CancellationToken cancellationToken)
    {
        var metadata = workspace.GetPublishedStaticMetadataRoot();
        var nginxRoot = Path.Combine(metadata, "nginx");
        Directory.CreateDirectory(nginxRoot);
        var configPath = Path.Combine(nginxRoot, "default.conf");
        await WriteAtomicallyAsync(configPath, BuildNginxConfiguration(sites, settings.ContainerPort), cancellationToken);
        await WriteIndexAsync(sites, cancellationToken);
    }

    private Task WriteIndexAsync(IReadOnlyDictionary<Guid, Guid> sites, CancellationToken cancellationToken)
        => WriteAtomicallyAsync(
            Path.Combine(workspace.GetPublishedStaticMetadataRoot(), "sites.json"),
            JsonSerializer.Serialize(sites, JsonOptions),
            cancellationToken);

    private async Task RestoreConfigurationAsync(
        IReadOnlyDictionary<Guid, Guid> previous,
        HostContainerScope scope,
        CancellationToken cancellationToken)
    {
        await WriteConfigurationAsync(previous, cancellationToken);
        await reloadContainer(scope, cancellationToken);
    }

    private async Task<MachineStaticServerEndpoint?> TryActivatePublicationAsync(
        IReadOnlyDictionary<Guid, Guid> updated,
        IReadOnlyDictionary<Guid, Guid> previous,
        HostContainerScope scope,
        CancellationToken cancellationToken)
    {
        await WriteConfigurationAsync(updated, cancellationToken);
        var endpoint = await ensureContainer(scope, cancellationToken);
        if (endpoint is not null && await reloadContainer(scope, cancellationToken)) return endpoint;
        await RestoreConfigurationAsync(previous, scope, CancellationToken.None);
        return null;
    }

    private Dictionary<Guid, Guid> ReadIndex()
    {
        var path = Path.Combine(workspace.GetPublishedStaticMetadataRoot(), "sites.json");
        if (!File.Exists(path)) return [];
        return JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The Machine Management Runner static-site index is invalid.");
    }

    private string ResolveOutputDirectory(Guid appId, Guid targetId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
            || relativePath.Replace('\\', '/').Split('/').Any(segment => segment == ".."))
            throw new InvalidOperationException("The declared static output directory must remain inside the deployed application artifact.");

        var root = Path.GetFullPath(workspace.GetAppRuntimeApplicationDirectory(appId, targetId));
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithin(root, resolved) || !Directory.Exists(resolved))
            throw new DirectoryNotFoundException("The declared static output directory is unavailable on this Runner.");
        return resolved;
    }

    private List<StaticFile> EnumerateSafeFiles(string sourceRoot, CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(sourceRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Static output may not be a symbolic link.");
        var files = new List<StaticFile>();
        var stack = new Stack<string>();
        stack.Push(sourceRoot);
        long totalBytes = 0;
        while (stack.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                AddStaticEntry(sourceRoot, entry, stack, files, ref totalBytes);
        }
        return files;
    }

    private void AddStaticEntry(
        string sourceRoot,
        string entry,
        Stack<string> pendingDirectories,
        ICollection<StaticFile> files,
        ref long totalBytes)
    {
        var attributes = File.GetAttributes(entry);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Static output may not contain symbolic links or reparse points.");
        if ((attributes & FileAttributes.Directory) != 0)
        {
            pendingDirectories.Push(entry);
            return;
        }

        var relative = Path.GetRelativePath(sourceRoot, entry).Replace('\\', '/');
        if (!PublishedStaticOutputFilePolicy.IsAllowedFilePath(relative))
            throw new InvalidOperationException($"Static output contains a private or unsupported file: {relative}");
        var length = new FileInfo(entry).Length;
        totalBytes = checked(totalBytes + length);
        if (files.Count >= settings.MaximumPublishedFiles || totalBytes > settings.MaximumPublishedFileBytes)
            throw new InvalidOperationException("Static output exceeds the configured Runner publication budget.");
        files.Add(new StaticFile(entry, relative, length));
    }

    private static void CopySafeFiles(IEnumerable<StaticFile> files, string destinationRoot, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(destinationRoot, destination))
                throw new UnauthorizedAccessException("Static output path escapes its publication staging directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file.SourcePath, destination, overwrite: false);
        }
    }

    private static async Task WriteAtomicallyAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var staging = path + ".tmp";
        await File.WriteAllTextAsync(staging, contents, new UTF8Encoding(false), cancellationToken);
        File.Move(staging, path, overwrite: true);
    }

    private void CleanupOldReleases(Guid appId, Guid current, Guid? previous)
    {
        var root = workspace.GetPublishedStaticSiteVersionsRoot(appId);
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (Guid.TryParse(Path.GetFileName(directory), out var id) && id != current && id != previous)
                DeleteTreeWithoutFollowingLinks(directory);
        }
    }

    private static void DeleteTreeWithoutFollowingLinks(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0
                && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0)
                DeleteTreeWithoutFollowingLinks(entry);
            else if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                Directory.Delete(entry);
            else
                File.Delete(entry);
        }
        Directory.Delete(path);
    }

    private static bool ValidIdentity(Guid operationId, HostContainerScope scope)
        => operationId != Guid.Empty && scope.AppId != Guid.Empty && scope.DeploymentTargetId != Guid.Empty
            && scope.MachineRunnerId != Guid.Empty;

    private static HostContainerScope CreateScope(Guid appId, Guid targetId)
        => new(Guid.Empty, Guid.Empty, appId, targetId, MachineRunnerId: CurrentRunnerId());

    private static Guid CurrentRunnerId()
        => Guid.TryParse(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID"), out var runnerId)
            ? runnerId
            : Guid.Empty;

    private static bool IsWithin(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || string.Equals(fullPath, fullRoot.TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private sealed record StaticFile(string SourcePath, string RelativePath, long Length);
}
