using Automaton.Studio.Server.Models;
using Serilog;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing app content storage on the file system.
/// Each user gets their own folder, and each app gets a subfolder within that.
/// This provides history tracking and full ownership without requiring GitHub.
/// </summary>
public class AppFileStorageService
{
    private const string SourceFolder = "source";
    private const string RuntimeFolder = "runtime";
    private const string VersionsFolder = "versions";
    private const string FrontendFolder = "frontend";
    private const string BackendFolder = "backend";
    private readonly IWebHostEnvironment webHostEnvironment;
    private readonly Serilog.ILogger logger;
    private readonly string baseStoragePath;

    /// <summary>
    /// Whitelist of allowed file extensions for security.
    /// Prevents users from uploading executable scripts (ps1, sh, php, etc.)
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Web files
        ".html", ".htm", ".css", ".js", ".mjs", ".json", ".xml",
        // Text/documentation
        ".txt", ".md", ".markdown",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".bmp",
        // Fonts
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        // Other web assets
        ".map", ".ts", ".tsx", ".jsx", ".vue", ".scss", ".sass", ".less",
        // Backend controller scripts (app-owned, executed on runner — not on server)
        ".py", ".bat"
    };

    public AppFileStorageService(IWebHostEnvironment webHostEnvironment)
    {
        this.webHostEnvironment = webHostEnvironment;
        this.logger = Log.ForContext<AppFileStorageService>();
        
        // Store apps in /AppData/apps/{userId}/{appId}/
        this.baseStoragePath = Path.Combine(webHostEnvironment.ContentRootPath, "AppData", "apps");
        
        // Ensure base directory exists
        Directory.CreateDirectory(baseStoragePath);
        
        logger.Information("App file storage initialized at: {Path}", baseStoragePath);
    }

    /// <summary>
    /// Get the storage path for a specific app
    /// </summary>
    private string GetAppPath(Guid userId, Guid appId)
    {
        return Path.Combine(baseStoragePath, userId.ToString(), appId.ToString());
    }

    private string GetSourcePath(Guid userId, Guid appId)
    {
        return Path.Combine(GetAppPath(userId, appId), SourceFolder);
    }

    private string GetRuntimePath(Guid userId, Guid appId)
    {
        return Path.Combine(GetAppPath(userId, appId), RuntimeFolder);
    }

    private string GetVersionsPath(Guid userId, Guid appId)
    {
        return Path.Combine(GetAppPath(userId, appId), VersionsFolder);
    }

    private string GetSourceFrontendPath(Guid userId, Guid appId)
    {
        return Path.Combine(GetSourcePath(userId, appId), FrontendFolder);
    }

    private string GetSourceBackendPath(Guid userId, Guid appId)
    {
        return Path.Combine(GetSourcePath(userId, appId), BackendFolder);
    }

    private string GetRuntimeFrontendPath(Guid userId, Guid appId)
    {
        return Path.Combine(GetRuntimePath(userId, appId), FrontendFolder);
    }

    private string GetRuntimeBackendPath(Guid userId, Guid appId)
    {
        return Path.Combine(GetRuntimePath(userId, appId), BackendFolder);
    }

    private static bool IsSensitiveAppFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);

        return fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".secret", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/secrets/", StringComparison.OrdinalIgnoreCase);
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

            // Save index.html only when content is provided. Editable frontend content belongs in source/frontend/.
            if (!string.IsNullOrEmpty(definition.HtmlContent))
            {
                var sourceFrontendPath = GetSourceFrontendPath(userId, appId);
                Directory.CreateDirectory(sourceFrontendPath);
                var indexPath = Path.Combine(sourceFrontendPath, "index.html");
                await File.WriteAllTextAsync(indexPath, definition.HtmlContent);
            }

            // Save metadata file
            var metadataPath = Path.Combine(appPath, "_metadata.json");
            var metadata = new
            {
                version = definition.Version,
                settings = definition.Settings,
                github = definition.GitHub,
                connectedFlowIds = definition.ConnectedFlowIds,
                lastModified = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

            logger.Information("Saved app {AppId} for user {UserId} to file system", appId, userId);
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
        try
        {
            var sourcePath = GetSourcePath(userId, appId);
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"App source directory not found: {sourcePath}");
            }

            var versionsPath = GetVersionsPath(userId, appId);
            var versionFolder = $"v{versionNumber:D3}";
            var targetPath = Path.Combine(versionsPath, versionFolder, "files");

            Directory.CreateDirectory(targetPath);

            foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
                var targetFilePath = Path.Combine(targetPath, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetFilePath);

                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(sourceFile, targetFilePath, overwrite: true);
            }

            logger.Information("Created app version snapshot v{VersionNumber} for app {AppId}", versionNumber, appId);

            return Task.FromResult(Path.Combine("versions", versionFolder));
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to create app version snapshot v{VersionNumber} for app {AppId}", versionNumber, appId);
            throw;
        }
    }

    /// <summary>
    /// Restore app files from a version snapshot back to the current app folder.
    /// </summary>
    public async Task RestoreVersionAsync(Guid userId, Guid appId, int versionNumber)
    {
        try
        {
            var sourcePath = GetSourcePath(userId, appId);
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"App source directory not found: {sourcePath}");
            }

            var versionFolder = $"v{versionNumber:D3}";
            var versionSnapshotPath = Path.Combine(GetVersionsPath(userId, appId), versionFolder, "files");

            if (!Directory.Exists(versionSnapshotPath))
            {
                throw new DirectoryNotFoundException($"Version snapshot not found: {versionSnapshotPath}");
            }

            foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }

            foreach (var dir in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }

            // Copy files from version snapshot to current
            foreach (var sourceFile in Directory.EnumerateFiles(versionSnapshotPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(versionSnapshotPath, sourceFile);
                var targetFilePath = Path.Combine(sourcePath, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetFilePath);

                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(sourceFile, targetFilePath, overwrite: true);
            }

            logger.Information("Restored app {AppId} from version v{VersionNumber}", appId, versionNumber);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to restore app {AppId} from version v{VersionNumber}", appId, versionNumber);
            throw;
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
            var metadataPath = Path.Combine(appPath, "_metadata.json");
            AppDefinition definition;

            if (File.Exists(metadataPath))
            {
                var metadataJson = await File.ReadAllTextAsync(metadataPath);
                var metadata = JsonSerializer.Deserialize<AppMetadata>(metadataJson);
                
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

            // Load editable index.html content from source/frontend/.
            var sourceIndexPath = Path.Combine(GetSourceFrontendPath(userId, appId), "index.html");
            if (File.Exists(sourceIndexPath))
            {
                definition.HtmlContent = await File.ReadAllTextAsync(sourceIndexPath);
            }

            logger.Information("Loaded app {AppId} for user {UserId} from file system", appId, userId);
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
                logger.Information("Deleted app {AppId} for user {UserId} from file system", appId, userId);
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
    /// Get the public runtime file system path for an app.
    /// </summary>
    public string GetAppRuntimeDirectoryPath(Guid userId, Guid appId)
    {
        return GetRuntimePath(userId, appId);
    }

    /// <summary>
    /// Get the editable frontend source path for an app.
    /// </summary>
    public string GetAppSourceFrontendDirectoryPath(Guid userId, Guid appId)
    {
        return GetSourceFrontendPath(userId, appId);
    }

    /// <summary>
    /// Get the editable backend source path for an app.
    /// </summary>
    public string GetAppSourceBackendDirectoryPath(Guid userId, Guid appId)
    {
        return GetSourceBackendPath(userId, appId);
    }

    /// <summary>
    /// Get the public frontend runtime path for an app.
    /// </summary>
    public string GetAppRuntimeFrontendDirectoryPath(Guid userId, Guid appId)
    {
        return GetRuntimeFrontendPath(userId, appId);
    }

    /// <summary>
    /// Get the runner backend runtime path for an app.
    /// </summary>
    public string GetAppRuntimeBackendDirectoryPath(Guid userId, Guid appId)
    {
        return GetRuntimeBackendPath(userId, appId);
    }

    /// <summary>
    /// Get the version snapshot file system path for an app.
    /// </summary>
    public string GetAppVersionDirectoryPath(Guid userId, Guid appId)
    {
        return GetVersionsPath(userId, appId);
    }

    /// <summary>
    /// Returns true when the app has an editable source folder.
    /// </summary>
    public bool AppSourceExists(Guid userId, Guid appId)
    {
        return Directory.Exists(GetSourcePath(userId, appId));
    }

    /// <summary>
    /// Returns true when the app has a deployed runtime folder.
    /// </summary>
    public bool AppRuntimeExists(Guid userId, Guid appId)
    {
        return Directory.Exists(GetRuntimePath(userId, appId));
    }

    /// <summary>
    /// Copy editable source files from one app to another. Runtime and version files are intentionally excluded.
    /// </summary>
    public Task CopySourceFilesAsync(Guid srcUserId, Guid srcAppId, Guid dstUserId, Guid dstAppId)
    {
        var srcPath = GetSourcePath(srcUserId, srcAppId);
        var dstPath = GetSourcePath(dstUserId, dstAppId);
        return CopyDirectoryFilesAsync(srcPath, dstPath, "source files", srcAppId, srcUserId, dstAppId, dstUserId);
    }

    /// <summary>
    /// Get all editable source files for an app.
    /// </summary>
    public Task<IEnumerable<string>> GetSourceFilesAsync(Guid userId, Guid appId)
    {
        return GetFilesAsync(GetSourcePath(userId, appId));
    }

    /// <summary>
    /// Read an editable source file for an app.
    /// </summary>
    public Task<string?> ReadSourceFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileAsync(GetSourcePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Write an editable source file for an app.
    /// </summary>
    public Task WriteSourceFileAsync(Guid userId, Guid appId, string relativePath, string content)
    {
        return WriteFileAsync(GetSourcePath(userId, appId), relativePath, content, validateExtension: true);
    }

    /// <summary>
    /// Delete an editable source file for an app.
    /// </summary>
    public Task DeleteSourceFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return DeleteFileAsync(GetSourcePath(userId, appId), relativePath);
    }

    /// <summary>
    /// Get all deployed frontend runtime files for an app.
    /// </summary>
    public Task<IEnumerable<string>> GetRuntimeFilesAsync(Guid userId, Guid appId)
    {
        return GetFilesAsync(GetRuntimeFrontendPath(userId, appId));
    }

    /// <summary>
    /// Get all deployed backend runtime files for an app.
    /// </summary>
    public Task<IEnumerable<string>> GetRuntimeBackendFilesAsync(Guid userId, Guid appId)
    {
        return GetFilesAsync(GetRuntimeBackendPath(userId, appId));
    }

    /// <summary>
    /// Read a deployed frontend runtime file for an app.
    /// </summary>
    public Task<string?> ReadRuntimeFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileAsync(GetRuntimeFrontendPath(userId, appId), relativePath);
    }

    /// <summary>
    /// Read a deployed backend runtime file for an app.
    /// </summary>
    public Task<string?> ReadRuntimeBackendFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileAsync(GetRuntimeBackendPath(userId, appId), relativePath);
    }

    /// <summary>
    /// Read a deployed frontend runtime file as bytes for public serving.
    /// </summary>
    public Task<byte[]?> ReadRuntimeFileBytesAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileBytesAsync(GetRuntimeFrontendPath(userId, appId), relativePath);
    }

    /// <summary>
    /// Write a deployed frontend runtime file for an app.
    /// </summary>
    public Task WriteRuntimeFileAsync(Guid userId, Guid appId, string relativePath, string content)
    {
        return WriteFileAsync(GetRuntimeFrontendPath(userId, appId), relativePath, content, validateExtension: true);
    }

    /// <summary>
    /// Delete a deployed frontend runtime file for an app.
    /// </summary>
    public Task DeleteRuntimeFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return DeleteFileAsync(GetRuntimeFrontendPath(userId, appId), relativePath);
    }

    /// <summary>
    /// Get all version snapshot files for an app.
    /// Paths are relative to the versions root.
    /// </summary>
    public Task<IEnumerable<string>> GetVersionFilesAsync(Guid userId, Guid appId)
    {
        return GetFilesAsync(GetVersionsPath(userId, appId));
    }

    /// <summary>
    /// Read a version snapshot file for an app.
    /// Path is relative to the versions root.
    /// </summary>
    public Task<string?> ReadVersionFileAsync(Guid userId, Guid appId, string relativePath)
    {
        return ReadFileAsync(GetVersionsPath(userId, appId), relativePath);
    }

    /// <summary>
    /// Remove all deployed frontend runtime files while preserving source, backend runtime, and versions.
    /// </summary>
    public Task ClearRuntimeAsync(Guid userId, Guid appId)
    {
        return ClearFrontendRuntimeAsync(userId, appId);
    }

    /// <summary>
    /// Remove all deployed frontend runtime files while preserving source, backend runtime, and versions.
    /// </summary>
    public Task ClearFrontendRuntimeAsync(Guid userId, Guid appId)
    {
        var runtimePath = GetRuntimeFrontendPath(userId, appId);
        if (Directory.Exists(runtimePath))
        {
            Directory.Delete(runtimePath, recursive: true);
        }

        Directory.CreateDirectory(runtimePath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Rebuild the runner backend runtime package from editable source/backend files.
    /// </summary>
    public Task SyncSourceBackendToRuntimeBackendAsync(Guid userId, Guid appId)
    {
        var sourceBackendPath = GetSourceBackendPath(userId, appId);
        var runtimeBackendPath = GetRuntimeBackendPath(userId, appId);

        if (Directory.Exists(runtimeBackendPath))
        {
            Directory.Delete(runtimeBackendPath, recursive: true);
        }

        Directory.CreateDirectory(runtimeBackendPath);

        if (!Directory.Exists(sourceBackendPath))
        {
            logger.Information("No source/backend directory found for app {AppId}; runtime/backend left empty", appId);
            return Task.CompletedTask;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceBackendPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceBackendPath, sourceFile);
            if (IsSensitiveAppFile(relativePath))
                continue;

            var targetFilePath = Path.Combine(runtimeBackendPath, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            File.Copy(sourceFile, targetFilePath, overwrite: true);
        }

        logger.Information("Synced source/backend to runtime/backend for app {AppId}", appId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove all deployed backend runtime files while preserving source, frontend runtime, and versions.
    /// </summary>
    public Task ClearRuntimeBackendAsync(Guid userId, Guid appId)
    {
        var runtimePath = GetRuntimeBackendPath(userId, appId);
        if (Directory.Exists(runtimePath))
        {
            Directory.Delete(runtimePath, recursive: true);
        }

        Directory.CreateDirectory(runtimePath);
        return Task.CompletedTask;
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

    private class AppMetadata
    {
        public int Version { get; set; }
        public AppSettings? Settings { get; set; }
        public GitHubSettings? GitHub { get; set; }
        public IList<string>? ConnectedFlowIds { get; set; }
        public DateTime LastModified { get; set; }
    }

    // ── Backend controller file helpers ──────────────────────────────────────

    /// <summary>
    /// Returns the file extension for a given script language (e.g. "Python" → ".py").
    /// </summary>
    public static string GetControllerExtension(string language) => language.ToLowerInvariant() switch
    {
        "python" => ".py",
        "batch"  => ".bat",
        _        => ".txt"
    };

    /// <summary>
    /// Reads the controller code from the app's source/backend/ folder.
    /// File: AppData/apps/{userId}/{appId}/source/backend/{controllerName}{ext}
    /// </summary>
    public async Task<string?> ReadControllerCodeAsync(Guid userId, Guid appId, string controllerName, string language)
    {
        var appPath = GetSourcePath(userId, appId);
        var backendPath = GetSourceBackendPath(userId, appId);
        var ext = GetControllerExtension(language);
        var filePath = Path.Combine(backendPath, controllerName + ext);
        if (!File.Exists(filePath)) return null;
        return await File.ReadAllTextAsync(filePath);
    }

    /// <summary>
    /// Writes controller code to the app's source/backend/ folder.
    /// </summary>
    public async Task WriteControllerCodeAsync(Guid userId, Guid appId, string controllerName, string language, string code)
    {
        var appPath = GetSourcePath(userId, appId);
        var backendPath = GetSourceBackendPath(userId, appId);
        Directory.CreateDirectory(backendPath);

        var ext = GetControllerExtension(language);
        var filePath = Path.Combine(backendPath, controllerName + ext);

        // Security: ensure path stays within app directory
        if (!Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(appPath)))
            throw new UnauthorizedAccessException("Access to files outside app directory is not allowed");

        await File.WriteAllTextAsync(filePath, code);
        logger.Information("Wrote controller {Name} for app {AppId}", controllerName, appId);
    }

    /// <summary>
    /// Deletes a controller code file from the app's backend/ folder.
    /// </summary>
    public Task DeleteControllerCodeAsync(Guid userId, Guid appId, string controllerName, string language)
    {
        var ext = GetControllerExtension(language);
        var filePath = Path.Combine(GetSourceBackendPath(userId, appId), controllerName + ext);
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Lists all controller script files in the app's source/backend/ folder.
    /// </summary>
    public async Task<IReadOnlyList<BackendScriptFile>> ListBackendFilesAsync(Guid userId, Guid appId)
    {
        var backendPath = GetSourceBackendPath(userId, appId);
        return await ListBackendFilesFromDirectoryAsync(backendPath);
    }

    /// <summary>
    /// Lists all script files in the app's runtime/backend/ package.
    /// </summary>
    public async Task<IReadOnlyList<BackendScriptFile>> ListRuntimeBackendFilesAsync(Guid userId, Guid appId)
    {
        var backendPath = GetRuntimeBackendPath(userId, appId);
        return await ListBackendFilesFromDirectoryAsync(backendPath);
    }

    private static async Task<IReadOnlyList<BackendScriptFile>> ListBackendFilesFromDirectoryAsync(string backendPath)
    {
        if (!Directory.Exists(backendPath)) return [];

        var results = new List<BackendScriptFile>();
        // SearchOption.AllDirectories so backend/shared/*.py files are included
        foreach (var file in Directory.GetFiles(backendPath, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".py" or ".bat" or ".txt")
            {
                var code = await File.ReadAllTextAsync(file);
                // Use forward-slash relative path as ControllerName (e.g. "shared/db")
                // so the runner can reconstruct the subdirectory
                var relativePath = Path.GetRelativePath(backendPath, file);
                var controllerName = Path.ChangeExtension(relativePath, null).Replace('\\', '/');
                results.Add(new BackendScriptFile(
                    controllerName,
                    code,
                    GetLanguageFromExtension(ext)));
            }
        }
        return results;
    }

    /// <summary>
    /// Finds a controller file by name (any supported extension) in the app's backend/ folder.
    /// Returns (code, language) or null if not found.
    /// File: AppData/apps/{userId}/{appId}/source/backend/{controllerName}.{py|bat|...}
    /// </summary>
    public async Task<(string Code, string Language)?> FindControllerFileAsync(Guid userId, Guid appId, string controllerName)
    {
        var backendPath = GetSourceBackendPath(userId, appId);
        if (!Directory.Exists(backendPath)) return null;

        foreach (var ext in new[] { ".py", ".bat", ".txt" })
        {
            var filePath = Path.Combine(backendPath, controllerName + ext);
            if (File.Exists(filePath))
            {
                var code = await File.ReadAllTextAsync(filePath);
                return (code, GetLanguageFromExtension(ext));
            }
        }
        return null;
    }

    /// <summary>Maps a file extension to a language name.</summary>
    public static string GetLanguageFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".py"  => "Python",
        ".bat" => "Batch",
        _      => "Text"
    };

    private Task CopyDirectoryFilesAsync(
        string srcPath,
        string dstPath,
        string copyKind,
        Guid srcAppId,
        Guid srcUserId,
        Guid dstAppId,
        Guid dstUserId)
    {
        Directory.CreateDirectory(dstPath);

        if (!Directory.Exists(srcPath))
        {
            logger.Warning("Source app {CopyKind} directory not found when copying: {SrcPath}", copyKind, srcPath);
            return Task.CompletedTask;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(srcPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(srcPath, sourceFile);
            if (IsSensitiveAppFile(relativePath))
                continue;

            var targetFilePath = Path.Combine(dstPath, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetFilePath);

            if (!string.IsNullOrEmpty(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            File.Copy(sourceFile, targetFilePath, overwrite: true);
        }

        logger.Information("Copied app {CopyKind} from {SrcAppId} ({SrcUser}) to {DstAppId} ({DstUser})",
            copyKind, srcAppId, srcUserId, dstAppId, dstUserId);

        return Task.CompletedTask;
    }

    private static Task<IEnumerable<string>> GetFilesAsync(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_metadata.json"))
            .Select(f => Path.GetRelativePath(rootPath, f))
            .ToList()
            .AsEnumerable();

        return Task.FromResult(files);
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

    private static async Task WriteFileAsync(string rootPath, string relativePath, string content, bool validateExtension)
    {
        if (validateExtension)
        {
            ValidateFileExtension(relativePath);
        }

        var filePath = ResolveSafePath(rootPath, relativePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, content);
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
