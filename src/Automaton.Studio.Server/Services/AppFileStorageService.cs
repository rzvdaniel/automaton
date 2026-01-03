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
