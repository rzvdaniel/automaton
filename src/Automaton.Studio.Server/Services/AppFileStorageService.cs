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
        ".map", ".ts", ".tsx", ".jsx", ".vue", ".scss", ".sass", ".less"
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

    /// <summary>
    /// Save app definition to file system
    /// </summary>
    public async Task SaveAppAsync(Guid userId, Guid appId, AppDefinition definition)
    {
        try
        {
            var appPath = GetAppPath(userId, appId);
            Directory.CreateDirectory(appPath);

            // Save index.html
            var indexPath = Path.Combine(appPath, "index.html");
            await File.WriteAllTextAsync(indexPath, definition.HtmlContent ?? "");

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
            var appPath = GetAppPath(userId, appId);
            if (!Directory.Exists(appPath))
            {
                throw new DirectoryNotFoundException($"App directory not found: {appPath}");
            }

            var versionsPath = Path.Combine(appPath, "versions");
            var versionFolder = $"v{versionNumber:D3}";
            var targetPath = Path.Combine(versionsPath, versionFolder, "files");

            Directory.CreateDirectory(targetPath);

            var versionRoot = Path.GetFullPath(versionsPath);

            foreach (var sourceFile in Directory.EnumerateFiles(appPath, "*", SearchOption.AllDirectories))
            {
                var fullSourcePath = Path.GetFullPath(sourceFile);
                if (fullSourcePath.StartsWith(versionRoot))
                {
                    continue; // Avoid copying existing versions
                }

                var relativePath = Path.GetRelativePath(appPath, sourceFile);
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
            var appPath = GetAppPath(userId, appId);
            if (!Directory.Exists(appPath))
            {
                throw new DirectoryNotFoundException($"App directory not found: {appPath}");
            }

            var versionFolder = $"v{versionNumber:D3}";
            var versionSnapshotPath = Path.Combine(appPath, "versions", versionFolder, "files");

            if (!Directory.Exists(versionSnapshotPath))
            {
                throw new DirectoryNotFoundException($"Version snapshot not found: {versionSnapshotPath}");
            }

            // Delete current files (except versions folder)
            var versionsPath = Path.Combine(appPath, "versions");
            var currentFiles = Directory.GetFiles(appPath, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFullPath(f).StartsWith(Path.GetFullPath(versionsPath)))
                .ToList();

            foreach (var file in currentFiles)
            {
                File.Delete(file);
            }

            // Remove empty directories (except versions)
            foreach (var dir in Directory.GetDirectories(appPath, "*", SearchOption.AllDirectories))
            {
                var fullDirPath = Path.GetFullPath(dir);
                if (!fullDirPath.StartsWith(Path.GetFullPath(versionsPath)) && !Directory.EnumerateFileSystemEntries(fullDirPath).Any())
                {
                    Directory.Delete(fullDirPath);
                }
            }

            // Copy files from version snapshot to current
            foreach (var sourceFile in Directory.EnumerateFiles(versionSnapshotPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(versionSnapshotPath, sourceFile);
                var targetFilePath = Path.Combine(appPath, relativePath);
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

            // Load index.html content
            var indexPath = Path.Combine(appPath, "index.html");
            if (File.Exists(indexPath))
            {
                definition.HtmlContent = await File.ReadAllTextAsync(indexPath);
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
    /// Get the file system path for an app (useful for direct file access)
    /// </summary>
    public string GetAppDirectoryPath(Guid userId, Guid appId)
    {
        return GetAppPath(userId, appId);
    }

    /// <summary>
    /// Get all files in an app directory (for listing/inspection)
    /// </summary>
    public async Task<IEnumerable<string>> GetAppFilesAsync(Guid userId, Guid appId)
    {
        var appPath = GetAppPath(userId, appId);
        if (!Directory.Exists(appPath))
        {
            return Enumerable.Empty<string>();
        }

        // Get all files recursively, excluding metadata
        var files = Directory.GetFiles(appPath, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_metadata.json"))
            .Select(f => Path.GetRelativePath(appPath, f))
            .ToList();

        return await Task.FromResult(files);
    }

    /// <summary>
    /// Read a specific file from an app directory
    /// </summary>
    public async Task<string?> ReadAppFileAsync(Guid userId, Guid appId, string relativePath)
    {
        var appPath = GetAppPath(userId, appId);
        var filePath = Path.Combine(appPath, relativePath);
        
        // Security: ensure the path is within the app directory
        var fullPath = Path.GetFullPath(filePath);
        var fullAppPath = Path.GetFullPath(appPath);
        if (!fullPath.StartsWith(fullAppPath))
        {
            throw new UnauthorizedAccessException("Access to files outside app directory is not allowed");
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath);
    }

    /// <summary>
    /// Write a specific file to an app directory
    /// </summary>
    public async Task WriteAppFileAsync(Guid userId, Guid appId, string relativePath, string content)
    {
        var appPath = GetAppPath(userId, appId);
        var filePath = Path.Combine(appPath, relativePath);

        // Security: validate file extension to prevent executable uploads
        ValidateFileExtension(relativePath);

        // Security: ensure the path is within the app directory
        var fullPath = Path.GetFullPath(filePath);
        var fullAppPath = Path.GetFullPath(appPath);
        if (!fullPath.StartsWith(fullAppPath))
        {
            throw new UnauthorizedAccessException("Access to files outside app directory is not allowed");
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, content);
    }

    /// <summary>
    /// Validates that the file extension is in the allowed whitelist.
    /// Throws InvalidOperationException if the extension is not allowed.
    /// </summary>
    private static void ValidateFileExtension(string relativePath)
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

    /// <summary>
    /// Delete a specific file from an app directory
    /// </summary>
    public async Task DeleteAppFileAsync(Guid userId, Guid appId, string relativePath)
    {
        var appPath = GetAppPath(userId, appId);
        var filePath = Path.Combine(appPath, relativePath);
        
        // Security: ensure the path is within the app directory
        var fullPath = Path.GetFullPath(filePath);
        var fullAppPath = Path.GetFullPath(appPath);
        if (!fullPath.StartsWith(fullAppPath))
        {
            throw new UnauthorizedAccessException("Access to files outside app directory is not allowed");
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        await Task.CompletedTask;
    }

    private class AppMetadata
    {
        public int Version { get; set; }
        public AppSettings? Settings { get; set; }
        public GitHubSettings? GitHub { get; set; }
        public IList<string>? ConnectedFlowIds { get; set; }
        public DateTime LastModified { get; set; }
    }
}
