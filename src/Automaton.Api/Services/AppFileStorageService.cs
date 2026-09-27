using Automaton.Api.Models;
using Serilog;
using System.Text.Json;

namespace Automaton.Api.Services;

/// <summary>
/// Service for managing app content storage on the file system.
/// Each user gets their own folder, and each app gets a subfolder within that.
/// This provides history tracking and full ownership without requiring GitHub.
/// </summary>
public class AppFileStorageService
{
    private const string SourceFolder = "source";
    private const string LiveFolder = "live";
    private const string VersionsFolder = "versions";
    public const int MaximumRetainedVersionSnapshots = 10;
    private readonly IWebHostEnvironment webHostEnvironment;
    private readonly Serilog.ILogger logger;
    private readonly string baseStoragePath;
    private readonly SettingsService settingsService;

    /// <summary>
    /// Whitelist of allowed file extensions for security.
    /// Prevents users from uploading executable scripts (ps1, sh, php, etc.)
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Web files
        ".html", ".htm", ".css", ".js", ".mjs", ".json", ".xml", ".webmanifest",
        // Text/documentation
        ".txt", ".md", ".markdown",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".bmp",
        // Fonts
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        // Other web assets
        ".map", ".ts", ".tsx", ".jsx", ".vue", ".scss", ".sass", ".less",
        // Common application source and configuration files. Storing source does
        // not execute it; execution is defined by the application contract.
        ".py", ".bat", ".cs", ".csproj", ".sln", ".fs", ".fsproj", ".go", ".rs", ".java", ".kt",
        ".toml", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".sh"
    };

    public AppFileStorageService(IWebHostEnvironment webHostEnvironment, SettingsService settingsService)
    {
        this.webHostEnvironment = webHostEnvironment;
        this.settingsService = settingsService;
        this.logger = Log.ForContext<AppFileStorageService>();
        
        // Store apps in /AppData/apps/{userId}/{appId}/
        this.baseStoragePath = Path.Combine(webHostEnvironment.ContentRootPath, "AppData", "apps");
        
        // Ensure base directory exists
        Directory.CreateDirectory(baseStoragePath);
        
        logger.Debug("App file storage initialized at: {Path}", baseStoragePath);
    }

    /// <summary>
    /// Get the storage path for a specific app
    /// </summary>
    private string GetAppPath(Guid userId, Guid appId)
    {
        return Path.Combine(baseStoragePath, userId.ToString(), appId.ToString());
    }

    private string GetMetadataPath(Guid userId, Guid appId)
    {
        return Path.Combine(GetAppPath(userId, appId), "_metadata.json");
    }

    private string GetSourcePath(Guid userId, Guid appId)
    {
        return Path.Combine(GetAppPath(userId, appId), SourceFolder);
    }

    private string GetLivePath(Guid userId, Guid appId)
    {
        return Path.Combine(GetAppPath(userId, appId), LiveFolder);
    }

    private string GetVersionsPath(Guid userId, Guid appId)
    {
        return Path.Combine(GetAppPath(userId, appId), VersionsFolder);
    }

    /// <summary>
    /// Save app definition to file system
    /// </summary>
    public async Task SaveAppAsync(Guid userId, Guid appId, AppDefinition definition)
    {
        try
        {
            var appPath = GetAppPath(userId, appId);
            Directory.CreateDirectory(appPath);

            // Preserve the legacy definition field as an ordinary source-root index file.
            if (!string.IsNullOrEmpty(definition.HtmlContent))
            {
                var sourcePath = GetSourcePath(userId, appId);
                await AppArchiveStorageBounds.WriteBoundedTextFileAsync(sourcePath, "index.html", definition.HtmlContent);
            }

            var metadata = await LoadStorageMetadataAsync(userId, appId) ?? CreateDefaultStorageMetadata();
            metadata.Version = definition.Version;
            metadata.Settings = definition.Settings;
            metadata.GitHub = definition.GitHub;
            metadata.ConnectedFlowIds = definition.ConnectedFlowIds;
            metadata.LastModified = DateTime.UtcNow;
            await SaveStorageMetadataAsync(userId, appId, metadata);

            logger.Debug("Saved app {AppId} for user {UserId} to file system", appId, userId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to save app {AppId} for user {UserId}", appId, userId);
            throw;
        }
    }

    /// <summary>
    /// Create an immutable snapshot of the current app files for versioning.
    /// </summary>
    public Task<string> CreateVersionSnapshotAsync(Guid userId, Guid appId, int versionNumber)
    {
        string? stagingPath = null;
        try
        {
            var sourcePath = GetSourcePath(userId, appId);
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"App source directory not found: {sourcePath}");
            }

            AppArchiveStorageBounds.ValidateDirectory(sourcePath);
            var versionsPath = GetVersionsPath(userId, appId);
            Directory.CreateDirectory(versionsPath);
            var versionFolder = $"v{versionNumber:D3}";
            var targetPath = Path.Combine(versionsPath, versionFolder);
            stagingPath = Path.Combine(versionsPath, $".staging-{versionFolder}-{Guid.NewGuid():N}");
            var stagingFilesPath = Path.Combine(stagingPath, "files");

            Directory.CreateDirectory(stagingFilesPath);

            foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
                var targetFilePath = Path.Combine(stagingFilesPath, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetFilePath);

                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(sourceFile, targetFilePath, overwrite: true);
            }

            AppArchiveStorageBounds.ValidateDirectory(stagingFilesPath);
            Directory.Move(stagingPath, targetPath);
            stagingPath = null;
            logger.Debug("Created app version snapshot v{VersionNumber} for app {AppId}", versionNumber, appId);

            return Task.FromResult(Path.Combine("versions", versionFolder));
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to create app version snapshot v{VersionNumber} for app {AppId}", versionNumber, appId);
            throw;
        }
        finally
        {
            TryDeleteSnapshotStagingDirectory(stagingPath, versionNumber, appId);
        }
    }

    public Task PruneVersionSnapshotsAsync(Guid userId, Guid appId)
    {
        var versionsPath = GetVersionsPath(userId, appId);
        if (!Directory.Exists(versionsPath))
            return Task.CompletedTask;

        var oldSnapshots = Directory.EnumerateDirectories(versionsPath)
            .Select(path => new { Path = path, Version = GetSnapshotVersion(Path.GetFileName(path)) })
            .Where(snapshot => snapshot.Version.HasValue)
            .OrderByDescending(snapshot => snapshot.Version)
            .Skip(MaximumRetainedVersionSnapshots);

        foreach (var snapshot in oldSnapshots)
            Directory.Delete(snapshot.Path, recursive: true);

        return Task.CompletedTask;
    }

    private static int? GetSnapshotVersion(string directoryName) =>
        directoryName.StartsWith('v') &&
        int.TryParse(directoryName.AsSpan(1), out var version) &&
        version > 0
            ? version
            : null;

    private void TryDeleteSnapshotStagingDirectory(string? stagingPath, int versionNumber, Guid appId)
    {
        if (stagingPath is null || !Directory.Exists(stagingPath))
            return;

        try
        {
            Directory.Delete(stagingPath, recursive: true);
        }
        catch (Exception exception)
        {
            logger.Warning(exception,
                "Could not remove staging files for app {AppId} version {VersionNumber}", appId, versionNumber);
        }
    }

    /// <summary>
    /// Load app definition from file system
    /// </summary>
    public async Task<AppDefinition?> LoadAppAsync(Guid userId, Guid appId)
    {
        try
        {
            var appPath = GetAppPath(userId, appId);
            
            if (!Directory.Exists(appPath))
            {
                logger.Warning("App path not found: {Path}", appPath);
                return null;
            }

            // Load metadata
            AppDefinition definition;
            var metadata = await LoadStorageMetadataAsync(userId, appId);
            if (metadata != null)
            {
                definition = new AppDefinition
                {
                    Version = metadata?.Version ?? 1,
                    Settings = metadata?.Settings ?? new AppSettings(),
                    GitHub = metadata?.GitHub,
                    ConnectedFlowIds = metadata?.ConnectedFlowIds ?? new List<string>()
                };
            }
            else
            {
                definition = new AppDefinition();
            }

            // Load the optional legacy definition field from the generic source root.
            var sourceIndexPath = Path.Combine(GetSourcePath(userId, appId), "index.html");
            if (File.Exists(sourceIndexPath))
            {
                definition.HtmlContent = await File.ReadAllTextAsync(sourceIndexPath);
            }

            logger.Debug("Loaded app {AppId} for user {UserId} from file system", appId, userId);
            return definition;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load app {AppId} for user {UserId}", appId, userId);
            throw;
        }
    }

    /// <summary>
    /// Delete app from file system
    /// </summary>
    public async Task DeleteAppAsync(Guid userId, Guid appId)
    {
        try
        {
            var appPath = GetAppPath(userId, appId);
            
            if (Directory.Exists(appPath))
            {
                Directory.Delete(appPath, recursive: true);
                logger.Debug("Deleted app {AppId} for user {UserId} from file system", appId, userId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to delete app {AppId} for user {UserId}", appId, userId);
            throw;
        }
    }

    /// <summary>
    /// Check if app exists on file system
    /// </summary>
    public bool AppExists(Guid userId, Guid appId)
    {
        var appPath = GetAppPath(userId, appId);
        return Directory.Exists(appPath);
    }

    /// <summary>
    /// Get the root file system path for an app.
    /// </summary>
    public string GetAppRootPath(Guid userId, Guid appId)
    {
        return GetAppPath(userId, appId);
    }

    /// <summary>
    /// Get the editable/forkable source file system path for an app.
    /// </summary>
    public string GetAppSourceDirectoryPath(Guid userId, Guid appId)
    {
        return GetSourcePath(userId, appId);
    }

    /// <summary>
    /// Get the live file system path for an app.
    /// </summary>
    public string GetAppRuntimeDirectoryPath(Guid userId, Guid appId)
    {
        return GetLivePath(userId, appId);
    }

    /// <summary>
    /// Get the complete editable source path for an app.
    /// </summary>
    public async Task<AppStorageMetadata> GetAppStorageMetadataAsync(Guid userId, Guid appId)
    {
        var metadata = await LoadStorageMetadataAsync(userId, appId) ?? CreateDefaultStorageMetadata();
        metadata.SourceAvailable = HasAnyEntries(GetSourcePath(userId, appId));
        metadata.RuntimeAvailable = metadata.RuntimeAvailable || HasAnyEntries(GetLivePath(userId, appId));
        return metadata;
    }

    public async Task UpdateAppStorageMetadataAsync(Guid userId, Guid appId, Action<AppStorageMetadata> update)
    {
        var metadata = await GetAppStorageMetadataAsync(userId, appId);
        update(metadata);
        metadata.LastModified = DateTime.UtcNow;
        await SaveStorageMetadataAsync(userId, appId, metadata);
    }

    /// <summary>
    /// Get the version snapshot file system path for an app.
    /// </summary>
    public string GetAppVersionDirectoryPath(Guid userId, Guid appId)
    {
        return GetVersionsPath(userId, appId);
    }

    public string? GetVersionSnapshotFilesPath(Guid userId, Guid appId, int versionNumber)
    {
        var versionFolder = Path.Combine(GetVersionsPath(userId, appId), $"v{versionNumber:D3}");
        var nestedFilesPath = Path.Combine(versionFolder, "files");
        if (Directory.Exists(nestedFilesPath))
        {
            return nestedFilesPath;
        }

        if (Directory.Exists(versionFolder))
        {
            return versionFolder;
        }

        return null;
    }

    public bool DirectoriesMatch(string currentPath, string snapshotPath)
    {
        if (!Directory.Exists(currentPath) || !Directory.Exists(snapshotPath))
        {
            return false;
        }

        var currentFiles = Directory.GetFiles(currentPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(currentPath, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var snapshotFiles = Directory.GetFiles(snapshotPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(snapshotPath, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!currentFiles.SequenceEqual(snapshotFiles, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var relativePath in currentFiles)
        {
            var currentFilePath = Path.Combine(currentPath, relativePath);
            var snapshotFilePath = Path.Combine(snapshotPath, relativePath);
            var currentBytes = File.ReadAllBytes(currentFilePath);
            var snapshotBytes = File.ReadAllBytes(snapshotFilePath);

            if (!currentBytes.AsSpan().SequenceEqual(snapshotBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true when the app has an editable source folder.
    /// </summary>
    public bool AppSourceExists(Guid userId, Guid appId)
    {
        return Directory.Exists(GetSourcePath(userId, appId));
    }

    /// <summary>
    /// Returns true when the app has a deployed live folder.
    /// </summary>
    public bool AppRuntimeExists(Guid userId, Guid appId)
    {
        return Directory.Exists(GetLivePath(userId, appId));
    }

    /// <summary>
    /// Copy portable editable source files from one app to another for duplicate/remix flows.
    /// This copies the complete portable source tree while excluding secret-like
    /// files and runtime/state folders.
    /// </summary>
    public async Task CopySourceFilesAsync(Guid srcUserId, Guid srcAppId, Guid dstUserId, Guid dstAppId)
    {
        var rules = await settingsService.GetEffectiveAppDeployRulesAsync();
        AppArchiveStorageBounds.ValidateDirectory(GetSourcePath(srcUserId, srcAppId));
        await CopyAppSubdirectoryFilesAsync(
            GetSourcePath(srcUserId, srcAppId),
            GetSourcePath(dstUserId, dstAppId),
            "application source files",
            srcAppId,
            srcUserId,
            dstAppId,
            dstUserId,
            relativePath => MatchesExcludeRule(relativePath, rules.RemixSourceExclude));
    }


    /// <summary>
    /// Get all editable source files for an app.
    /// </summary>
    public Task<IEnumerable<string>> GetSourceFilesAsync(Guid userId, Guid appId)
    {
        return GetFilesAsync(GetSourcePath(userId, appId));
    }

    /// <summary>
    /// Get all files under the app root, including source, live, versions, and metadata.
    /// Paths are relative to the app root. Callers may filter runtime folders based on their UI needs.
    /// </summary>
    public Task<IEnumerable<string>> GetAppRootEntriesAsync(Guid userId, Guid appId)
    {
        return GetEntriesAsync(GetAppPath(userId, appId));
    }

    /// <summary>
    /// Read a file directly from the app root, including metadata files.
    /// </summary>
    public Task<string?> ReadAppRootFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileAsync(GetAppPath(userId, appId), relativePath);
    }

    /// <summary>
    /// Read an editable source file for an app.
    /// </summary>
    public Task<string?> ReadSourceFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileAsync(GetSourcePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Read an editable source file as bytes for an app.
    /// </summary>
    public Task<byte[]?> ReadSourceFileBytesAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileBytesAsync(GetSourcePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Write an editable source file for an app.
    /// </summary>
    public async Task WriteSourceFileAsync(Guid userId, Guid appId, string relativePath, string content)
    {
        var sourcePath = GetSourcePath(userId, appId);
        ValidateFileExtension(relativePath);
        await AppArchiveStorageBounds.WriteBoundedTextFileAsync(sourcePath, relativePath, content);
        await MarkSourceModifiedAsync(userId, appId, relativePath);
    }

    /// <summary>
    /// Delete an editable source file for an app.
    /// </summary>
    public async Task DeleteSourceFileAsync(Guid userId, Guid appId, string relativePath)
    {
        await DeleteFileAsync(GetSourcePath(userId, appId), relativePath);
        await MarkSourceModifiedAsync(userId, appId, relativePath);
    }

    /// <summary>
    /// Delete an editable source directory for an app.
    /// </summary>
    public async Task DeleteSourceDirectoryAsync(Guid userId, Guid appId, string relativePath)
    {
        await DeleteDirectoryAsync(GetSourcePath(userId, appId), relativePath);
        await MarkSourceModifiedAsync(userId, appId, relativePath);
    }

    /// <summary>
    /// Remove all editable source files while preserving live files and versions.
    /// </summary>
    public async Task ClearSourceAsync(Guid userId, Guid appId)
    {
        var sourcePath = GetSourcePath(userId, appId);
        if (Directory.Exists(sourcePath))
        {
            Directory.Delete(sourcePath, recursive: true);
        }

        Directory.CreateDirectory(sourcePath);
        await UpdateAppStorageMetadataAsync(userId, appId, metadata =>
        {
            metadata.SourceModifiedAtUtc = DateTime.UtcNow;
        });
    }

    /// <summary>
    /// Get all deployed frontend live files for an app.
    /// </summary>
    public Task<IEnumerable<string>> GetRuntimeFilesAsync(Guid userId, Guid appId)
    {
        return GetFilesAsync(GetLivePath(userId, appId));
    }

    /// <summary>
    /// Read a deployed frontend live file for an app.
    /// </summary>
    public Task<string?> ReadRuntimeFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileAsync(GetLivePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Read a deployed frontend live file as bytes for public serving.
    /// </summary>
    public Task<byte[]?> ReadRuntimeFileBytesAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileBytesAsync(GetLivePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Write a deployed frontend live file for an app.
    /// </summary>
    public Task WriteRuntimeFileAsync(Guid userId, Guid appId, string relativePath, string content)
    {
        var runtimePath = GetLivePath(userId, appId);
        ValidateFileExtension(relativePath);
        return AppArchiveStorageBounds.WriteBoundedTextFileAsync(runtimePath, relativePath, content);
    }

    /// <summary>
    /// Delete a deployed frontend live file for an app.
    /// </summary>
    public Task DeleteRuntimeFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return DeleteFileAsync(GetLivePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Delete a deployed frontend live directory for an app.
    /// </summary>
    public Task DeleteRuntimeDirectoryAsync(Guid userId, Guid appId, string relativePath)
    {
        return DeleteDirectoryAsync(GetLivePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Remove all deployed frontend live files while preserving source, backend live files, and versions.
    /// </summary>
    public Task ClearRuntimeAsync(Guid userId, Guid appId)
    {
        var runtimePath = GetLivePath(userId, appId);
        if (Directory.Exists(runtimePath)) Directory.Delete(runtimePath, recursive: true);
        Directory.CreateDirectory(runtimePath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove all deployed frontend live files while preserving source, backend live files, and versions.
    /// </summary>
    public async Task ClearApplicationRuntimeAsync(Guid userId, Guid appId)
    {
        var runtimePath = GetLivePath(userId, appId);
        if (Directory.Exists(runtimePath))
        {
            Directory.Delete(runtimePath, recursive: true);
        }

        Directory.CreateDirectory(runtimePath);
        await UpdateAppStorageMetadataAsync(userId, appId, metadata =>
        {
            metadata.RuntimeAvailable = HasAnyEntries(runtimePath);
            metadata.RuntimeDeployedAtUtc = metadata.RuntimeAvailable ? metadata.RuntimeDeployedAtUtc : null;
        });
    }

    /// <summary>
    /// Validates that the file extension is in the allowed whitelist.
    /// Throws InvalidOperationException if the extension is not allowed.
    /// </summary>
    private static void ValidateFileExtension(string relativePath) =>
        ValidateFileExtensionPublic(relativePath);

    /// <summary>
    /// Validates that the file extension is in the allowed whitelist.
    /// Exposed as public so <see cref="AppScopedFileSystem"/> can reuse it without a service dependency.
    /// Throws <see cref="InvalidOperationException"/> if the extension is not allowed.
    /// </summary>
    public static void ValidateFileExtensionPublic(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);

        // Allow files without extension (like LICENSE, Makefile, etc.)
        if (string.IsNullOrEmpty(extension))
        {
            return;
        }

        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                $"File type '{extension}' is not allowed. " +
                $"Allowed types: {string.Join(", ", AllowedExtensions.Order())}");
        }
    }

    public bool HasAnySourceEntries(Guid userId, Guid appId)
    {
        return HasAnyEntries(GetSourcePath(userId, appId));
    }

    private Task MarkSourceModifiedAsync(Guid userId, Guid appId, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var modifiedAtUtc = DateTime.UtcNow;

        return UpdateAppStorageMetadataAsync(userId, appId, metadata =>
        {
            metadata.SourceModifiedAtUtc = modifiedAtUtc;
        });
    }

    private static bool HasAnyEntries(string path)
    {
        return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Any();
    }

    private static AppStorageMetadata CreateDefaultStorageMetadata()
    {
        return new AppStorageMetadata();
    }

    private async Task<AppStorageMetadata?> LoadStorageMetadataAsync(Guid userId, Guid appId)
    {
        var metadataPath = GetMetadataPath(userId, appId);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var metadataJson = await File.ReadAllTextAsync(metadataPath);
        using var document = JsonDocument.Parse(metadataJson);
        var schemaVersionProperty = document.RootElement.EnumerateObject()
            .FirstOrDefault(property => string.Equals(property.Name, nameof(AppStorageMetadata.MetadataSchemaVersion), StringComparison.OrdinalIgnoreCase));
        if (schemaVersionProperty.Value.ValueKind != JsonValueKind.Undefined
            && schemaVersionProperty.Value.GetInt32() >= 2)
        {
            return JsonSerializer.Deserialize<AppStorageMetadata>(metadataJson);
        }

        var legacy = JsonSerializer.Deserialize<LegacyAppStorageMetadata>(metadataJson);
        if (legacy is null)
        {
            return CreateDefaultStorageMetadata();
        }

        var sourceModifiedAtUtc = Max(legacy.Frontend?.SourceModifiedAtUtc, legacy.Backend?.SourceModifiedAtUtc);
        var runtimeDeployedAtUtc = Max(legacy.Frontend?.RuntimeDeployedAtUtc, legacy.Backend?.RuntimeDeployedAtUtc);
        var migrated = new AppStorageMetadata
        {
            MetadataSchemaVersion = 2,
            Version = legacy.Version,
            Settings = legacy.Settings,
            GitHub = legacy.GitHub,
            ConnectedFlowIds = legacy.ConnectedFlowIds,
            LastModified = legacy.LastModified,
            DevelopmentMode = legacy.DevelopmentMode,
            SourceAvailable = HasAnyEntries(GetSourcePath(userId, appId)),
            SourceModifiedAtUtc = sourceModifiedAtUtc,
            RuntimeSource = SelectLegacyRuntimeSource(legacy.Frontend, legacy.Backend),
            RuntimeAvailable = legacy.Frontend?.RuntimeAvailable == true || legacy.Backend?.RuntimeAvailable == true,
            RuntimeDeployedAtUtc = runtimeDeployedAtUtc
        };

        await SaveStorageMetadataAsync(userId, appId, migrated);
        return migrated;
    }

    private static DateTime? Max(DateTime? left, DateTime? right) =>
        left.HasValue && right.HasValue ? (left > right ? left : right) : left ?? right;

    private static string SelectLegacyRuntimeSource(LegacyComponentStorageMetadata? frontend, LegacyComponentStorageMetadata? backend)
    {
        var components = new[] { frontend, backend }.Where(component => component is not null).Cast<LegacyComponentStorageMetadata>().ToArray();
        var deployed = components.Where(component => component.RuntimeDeployedAtUtc.HasValue)
            .OrderByDescending(component => component.RuntimeDeployedAtUtc).ToArray();
        if (deployed.Length > 0)
            return NormalizeRuntimeSource(deployed[0].RuntimeSource);

        var available = components.Where(component => component.RuntimeAvailable).ToArray();
        if (available.Length == 1)
            return NormalizeRuntimeSource(available[0].RuntimeSource);

        var meaningful = components.Select(component => component.RuntimeSource)
            .Where(source => !string.IsNullOrWhiteSpace(source) && !string.Equals(source, Constants.AppRuntimeSources.Studio, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (meaningful.Length == 1)
            return NormalizeRuntimeSource(meaningful[0]);

        var values = components.Select(component => component.RuntimeSource)
            .Where(source => !string.IsNullOrWhiteSpace(source)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 1 ? NormalizeRuntimeSource(values[0]) : Constants.AppRuntimeSources.Studio;
    }

    private static string NormalizeRuntimeSource(string? source) => source switch
    {
        null => Constants.AppRuntimeSources.Studio,
        var value when string.Equals(value, "CliBuild", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, Constants.AppRuntimeSources.CliBuild, StringComparison.OrdinalIgnoreCase)
            => Constants.AppRuntimeSources.CliBuild,
        var value when string.Equals(value, "CliDeploy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, Constants.AppRuntimeSources.CliDeploy, StringComparison.OrdinalIgnoreCase)
            => Constants.AppRuntimeSources.CliDeploy,
        var value when string.Equals(value, "Studio", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, Constants.AppRuntimeSources.Studio, StringComparison.OrdinalIgnoreCase)
            => Constants.AppRuntimeSources.Studio,
        _ => source
    };

    private sealed class LegacyAppStorageMetadata
    {
        public int Version { get; set; } = 1;
        public AppSettings? Settings { get; set; }
        public GitHubSettings? GitHub { get; set; }
        public IList<string>? ConnectedFlowIds { get; set; }
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public string DevelopmentMode { get; set; } = Constants.AppDevelopmentModes.Studio;
        public LegacyComponentStorageMetadata? Frontend { get; set; }
        public LegacyComponentStorageMetadata? Backend { get; set; }
    }

    private sealed class LegacyComponentStorageMetadata
    {
        public DateTime? SourceModifiedAtUtc { get; set; }
        public string? RuntimeSource { get; set; }
        public bool RuntimeAvailable { get; set; }
        public DateTime? RuntimeDeployedAtUtc { get; set; }
    }

    private async Task SaveStorageMetadataAsync(Guid userId, Guid appId, AppStorageMetadata metadata)
    {
        var appPath = GetAppPath(userId, appId);
        Directory.CreateDirectory(appPath);
        var metadataPath = GetMetadataPath(userId, appId);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool MatchesExcludeRule(string relativePath, IEnumerable<string> rules)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = Path.GetFileName(normalized);

        foreach (var rawRule in rules)
        {
            if (MatchesExcludePattern(normalized, fileName, segments, rawRule))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesExcludePattern(
        string normalizedPath,
        string fileName,
        IReadOnlyCollection<string> segments,
        string? rawRule)
    {
        var rule = rawRule?.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(rule))
            return false;

        if (rule.EndsWith("/**", StringComparison.Ordinal))
            return MatchesDirectoryPrefix(normalizedPath, rule);

        if (rule.StartsWith("*.", StringComparison.Ordinal))
            return fileName.EndsWith(rule[1..], StringComparison.OrdinalIgnoreCase);

        if (rule.Contains('/', StringComparison.Ordinal))
            return string.Equals(normalizedPath, rule.Trim('/'), StringComparison.OrdinalIgnoreCase);

        return MatchesPathSegment(fileName, segments, rule);
    }

    private static bool MatchesDirectoryPrefix(string normalizedPath, string rule)
    {
        var prefix = rule[..^3].Trim('/');
        return normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedPath, prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPathSegment(string fileName, IEnumerable<string> segments, string rule)
        => string.Equals(fileName, rule, StringComparison.OrdinalIgnoreCase) ||
            segments.Any(segment => string.Equals(segment, rule, StringComparison.OrdinalIgnoreCase));

    private async Task CopyDirectoryFilesAsync(
        string srcPath,
        string dstPath,
        string copyKind,
        Guid srcAppId,
        Guid srcUserId,
        Guid dstAppId,
        Guid dstUserId,
        Func<string, bool>? shouldSkip = null)
    {
        await using var writeLock = await AppArchiveStorageBounds.AcquireWriteLockAsync(dstPath);
        Directory.CreateDirectory(dstPath);

        if (!Directory.Exists(srcPath))
        {
            logger.Warning("Source app {CopyKind} directory not found when copying: {SrcPath}", copyKind, srcPath);
            return;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(srcPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(srcPath, sourceFile);
            if (shouldSkip?.Invoke(relativePath) ?? false)
                continue;

            var targetFilePath = Path.Combine(dstPath, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetFilePath);

            if (!string.IsNullOrEmpty(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            AppArchiveStorageBounds.ValidateFileWrite(dstPath, relativePath, new FileInfo(sourceFile).Length);
            File.Copy(sourceFile, targetFilePath, overwrite: true);
        }

        logger.Debug("Copied app {CopyKind} from {SrcAppId} ({SrcUser}) to {DstAppId} ({DstUser})",
            copyKind, srcAppId, srcUserId, dstAppId, dstUserId);
    }

    private Task CopyAppSubdirectoryFilesAsync(
        string srcPath,
        string dstPath,
        string copyKind,
        Guid srcAppId,
        Guid srcUserId,
        Guid dstAppId,
        Guid dstUserId,
        Func<string, bool>? shouldSkip = null)
    {
        return CopyDirectoryFilesAsync(
            srcPath,
            dstPath,
            copyKind,
            srcAppId,
            srcUserId,
            dstAppId,
            dstUserId,
            shouldSkip);
    }

    private static Task<IEnumerable<string>> GetFilesAsync(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(rootPath, f))
            .ToList()
            .AsEnumerable();

        return Task.FromResult(files);
    }

    private static Task<IEnumerable<string>> GetEntriesAsync(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        var directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(rootPath, path).Replace('\\', '/') + "/");

        var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(rootPath, path).Replace('\\', '/'));

        var entries = directories
            .Concat(files)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .AsEnumerable();

        return Task.FromResult(entries);
    }

    private static async Task<string?> ReadFileAsync(string rootPath, string relativePath)
    {
        var filePath = ResolveSafePath(rootPath, relativePath);
        return File.Exists(filePath)
            ? await File.ReadAllTextAsync(filePath)
            : null;
    }

    private static async Task<byte[]?> ReadFileBytesAsync(string rootPath, string relativePath)
    {
        var filePath = ResolveSafePath(rootPath, relativePath);
        return File.Exists(filePath)
            ? await File.ReadAllBytesAsync(filePath)
            : null;
    }

    public Task WriteFileAtPathAsync(string rootPath, string relativePath, string content, bool validateExtension = true)
    {
        if (validateExtension)
            ValidateFileExtension(relativePath);
        return AppArchiveStorageBounds.WriteBoundedTextFileAsync(rootPath, relativePath, content);
    }

    private static Task DeleteFileAsync(string rootPath, string relativePath)
    {
        var filePath = ResolveSafePath(rootPath, relativePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFileAtPathAsync(string rootPath, string relativePath)
    {
        return DeleteFileAsync(rootPath, relativePath);
    }

    private static Task DeleteDirectoryAsync(string rootPath, string relativePath)
    {
        var directoryPath = ResolveSafePath(rootPath, relativePath);
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAtPathAsync(string rootPath, string relativePath)
    {
        return DeleteDirectoryAsync(rootPath, relativePath);
    }

    private static string ResolveSafePath(string rootPath, string relativePath)
    {
        var filePath = Path.Combine(rootPath, relativePath);
        var fullPath = Path.GetFullPath(filePath);
        var fullRootPath = Path.GetFullPath(rootPath);
        if (!fullPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access to files outside app directory is not allowed");
        }

        return fullPath;
    }
}
