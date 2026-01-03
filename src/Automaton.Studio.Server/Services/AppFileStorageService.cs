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

            if (definition.IsMultiFile && definition.Files != null)
            {
                // Save multi-file structure
                await SaveMultiFileAppAsync(appPath, definition);
            }
            else
            {
                // Save single-file app
                await SaveSingleFileAppAsync(appPath, definition);
            }

            // Save metadata file
            var metadataPath = Path.Combine(appPath, "_metadata.json");
            var metadata = new
            {
                isMultiFile = definition.IsMultiFile,
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
                    IsMultiFile = metadata?.IsMultiFile ?? false,
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

            // Load content
            if (definition.IsMultiFile)
            {
                await LoadMultiFileAppAsync(appPath, definition);
            }
            else
            {
                await LoadSingleFileAppAsync(appPath, definition);
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

    private async Task SaveSingleFileAppAsync(string appPath, AppDefinition definition)
    {
        var indexPath = Path.Combine(appPath, "index.html");
        await File.WriteAllTextAsync(indexPath, definition.HtmlContent ?? "");
    }

    private async Task LoadSingleFileAppAsync(string appPath, AppDefinition definition)
    {
        var indexPath = Path.Combine(appPath, "index.html");
        if (File.Exists(indexPath))
        {
            definition.HtmlContent = await File.ReadAllTextAsync(indexPath);
        }
    }

    private async Task SaveMultiFileAppAsync(string appPath, AppDefinition definition)
    {
        // Save index.html
        var indexPath = Path.Combine(appPath, "index.html");
        await File.WriteAllTextAsync(indexPath, definition.Files?.IndexHtml ?? "");

        // Save HTML files
        if (definition.Files?.HtmlFiles?.Any() == true)
        {
            foreach (var (fileName, content) in definition.Files.HtmlFiles)
            {
                var filePath = Path.Combine(appPath, fileName);
                await File.WriteAllTextAsync(filePath, content);
            }
        }

        // Save CSS files
        if (definition.Files?.CssFiles?.Any() == true)
        {
            var cssDir = Path.Combine(appPath, "css");
            Directory.CreateDirectory(cssDir);
            foreach (var (fileName, content) in definition.Files.CssFiles)
            {
                var filePath = Path.Combine(cssDir, fileName);
                await File.WriteAllTextAsync(filePath, content);
            }
        }

        // Save JS files
        if (definition.Files?.JsFiles?.Any() == true)
        {
            var jsDir = Path.Combine(appPath, "js");
            Directory.CreateDirectory(jsDir);
            foreach (var (fileName, content) in definition.Files.JsFiles)
            {
                var filePath = Path.Combine(jsDir, fileName);
                await File.WriteAllTextAsync(filePath, content);
            }
        }

        // Save assets
        if (definition.Files?.Assets?.Any() == true)
        {
            var assetsDir = Path.Combine(appPath, "assets");
            Directory.CreateDirectory(assetsDir);
            foreach (var (fileName, content) in definition.Files.Assets)
            {
                var filePath = Path.Combine(assetsDir, fileName);
                await File.WriteAllTextAsync(filePath, content);
            }
        }
    }

    private async Task LoadMultiFileAppAsync(string appPath, AppDefinition definition)
    {
        definition.Files = new AppFileStructure();

        // Load index.html
        var indexPath = Path.Combine(appPath, "index.html");
        if (File.Exists(indexPath))
        {
            definition.Files.IndexHtml = await File.ReadAllTextAsync(indexPath);
        }

        // Load additional HTML files (in root directory, not index.html)
        var htmlFiles = Directory.GetFiles(appPath, "*.html")
            .Where(f => Path.GetFileName(f) != "index.html");
        foreach (var htmlFile in htmlFiles)
        {
            var fileName = Path.GetFileName(htmlFile);
            definition.Files.HtmlFiles[fileName] = await File.ReadAllTextAsync(htmlFile);
        }

        // Load CSS files
        var cssDir = Path.Combine(appPath, "css");
        if (Directory.Exists(cssDir))
        {
            foreach (var cssFile in Directory.GetFiles(cssDir, "*.css"))
            {
                var fileName = Path.GetFileName(cssFile);
                definition.Files.CssFiles[fileName] = await File.ReadAllTextAsync(cssFile);
            }
        }

        // Load JS files
        var jsDir = Path.Combine(appPath, "js");
        if (Directory.Exists(jsDir))
        {
            foreach (var jsFile in Directory.GetFiles(jsDir, "*.js"))
            {
                var fileName = Path.GetFileName(jsFile);
                definition.Files.JsFiles[fileName] = await File.ReadAllTextAsync(jsFile);
            }
        }

        // Load assets
        var assetsDir = Path.Combine(appPath, "assets");
        if (Directory.Exists(assetsDir))
        {
            foreach (var assetFile in Directory.GetFiles(assetsDir))
            {
                var fileName = Path.GetFileName(assetFile);
                definition.Files.Assets[fileName] = await File.ReadAllTextAsync(assetFile);
            }
        }
    }

    private class AppMetadata
    {
        public bool IsMultiFile { get; set; }
        public int Version { get; set; }
        public AppSettings? Settings { get; set; }
        public GitHubSettings? GitHub { get; set; }
        public IList<string>? ConnectedFlowIds { get; set; }
        public DateTime LastModified { get; set; }
    }
}
