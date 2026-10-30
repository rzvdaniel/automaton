using System.Text.Json;
using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

/// <summary>
/// Reads and changes how the linked app appears in the public marketplace.
/// Publishing lists an app by default; this sets its text and images.
/// </summary>
public sealed class MarketplaceListingService(ConfigService config)
{
    public const long MaximumImageBytes = 2 * 1024 * 1024;
    public const int MaximumScreenshots = 8;

    public async Task<MarketplaceListing> ShowAsync(string directory, string? environmentName, CancellationToken ct = default)
    {
        var (client, appId) = CreateClient(directory, environmentName);
        using (client)
        {
            return ReadListing(appId, await client.GetAppAsync(appId, ct));
        }
    }

    public async Task<MarketplaceListing> SetAsync(
        string directory, string? environmentName, MarketplaceListingUpdate update, CancellationToken ct = default)
    {
        if (update.IsEmpty)
        {
            throw new InvalidOperationException(
                "Nothing to change. Pass at least one of --summary, --description, --category, --tags, --clear-category or --clear-tags.");
        }

        var (client, appId) = CreateClient(directory, environmentName);
        using (client)
        {
            await client.UpdateListingAsync(appId, update, ct);
            return ReadListing(appId, await client.GetAppAsync(appId, ct));
        }
    }

    public async Task<MarketplaceListing> SetCoverAsync(
        string directory, string? environmentName, string imagePath, CancellationToken ct = default)
    {
        var path = ValidateImage(imagePath);
        var (client, appId) = CreateClient(directory, environmentName);
        using (client)
        {
            await UploadAsync(path, (stream, name, type) => client.UploadListingCoverAsync(appId, stream, name, type, ct));
            return ReadListing(appId, await client.GetAppAsync(appId, ct));
        }
    }

    public async Task<MarketplaceListing> AddScreenshotsAsync(
        string directory, string? environmentName, IReadOnlyList<string> imagePaths, CancellationToken ct = default)
    {
        if (imagePaths.Count == 0)
        {
            throw new InvalidOperationException("Pass at least one image file.");
        }

        // Check every file first, so a missing or oversized file is caught before anything uploads.
        var paths = imagePaths.Select(ValidateImage).ToList();
        var (client, appId) = CreateClient(directory, environmentName);
        using (client)
        {
            var current = ReadListing(appId, await client.GetAppAsync(appId, ct));
            if (current.Screenshots.Count + paths.Count > MaximumScreenshots)
            {
                throw new InvalidOperationException(
                    $"An app can have up to {MaximumScreenshots} screenshots; it has {current.Screenshots.Count}. " +
                    "Remove some with 'automaton listing remove-screenshot <id>'.");
            }

            for (var index = 0; index < paths.Count; index++)
            {
                try
                {
                    await UploadAsync(paths[index], (stream, name, type) => client.UploadListingScreenshotAsync(appId, stream, name, type, ct));
                }
                catch (Exception exception) when (index > 0)
                {
                    throw new InvalidOperationException(
                        $"Uploaded {index} of {paths.Count} screenshots, then {imagePaths[index]} failed: {exception.Message}", exception);
                }
            }
            return ReadListing(appId, await client.GetAppAsync(appId, ct));
        }
    }

    public async Task<MarketplaceListing> RemoveScreenshotAsync(
        string directory, string? environmentName, string screenshotId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(screenshotId, out var id))
        {
            throw new InvalidOperationException(
                $"Not a screenshot ID: {screenshotId}. Run 'automaton listing show' to see the IDs.");
        }

        var (client, appId) = CreateClient(directory, environmentName);
        using (client)
        {
            await client.DeleteListingScreenshotAsync(appId, id.ToString(), ct);
            return ReadListing(appId, await client.GetAppAsync(appId, ct));
        }
    }

    private static async Task UploadAsync(string path, Func<Stream, string, string, Task<JsonElement>> upload)
    {
        await using var stream = File.OpenRead(path);
        await upload(stream, Path.GetFileName(path), ImageContentType(path));
    }

    public static string ImageContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };

    public static string ValidateImage(string imagePath)
    {
        var path = Path.GetFullPath(imagePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Image not found: {imagePath}");
        }
        if (ImageContentType(path) == "application/octet-stream")
        {
            throw new InvalidOperationException($"Unsupported image type: {imagePath}. Use PNG, JPG, WEBP or GIF.");
        }
        if (new FileInfo(path).Length > MaximumImageBytes)
        {
            throw new InvalidOperationException($"Image is larger than 2 MB: {imagePath}");
        }
        return path;
    }

    public static MarketplaceListing ReadListing(string appId, JsonElement app)
    {
        var tags = Text(app, "tags")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var screenshots = app.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array
            ? shots.EnumerateArray()
                .OrderBy(shot => shot.TryGetProperty("sortOrder", out var order) && order.ValueKind == JsonValueKind.Number ? order.GetInt32() : 0)
                .Select(shot => new MarketplaceScreenshot(Text(shot, "id") ?? "", Text(shot, "imageUrl") ?? ""))
                .ToList()
            : [];
        return new MarketplaceListing(
            appId,
            Text(app, "name") ?? appId,
            Text(app, "summary") ?? "",
            Text(app, "description") ?? "",
            Text(app, "category"),
            tags,
            Text(app, "thumbnailUrl"),
            screenshots);
    }

    private static string? Text(JsonElement value, string property)
        => value.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;

    private (AutomatonApiClient Client, string AppId) CreateClient(string directory, string? environmentName)
    {
        var project = config.TryLoadProjectConfig(directory)
            ?? throw new InvalidOperationException("automaton.json was not found.");
        var resolved = config.ResolveContext(project, directory, environmentName);
        if (string.IsNullOrWhiteSpace(resolved.AppId))
        {
            throw new InvalidOperationException("The project is not linked to an Automaton app.");
        }
        if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            throw new InvalidOperationException("Automaton server credentials are not configured. Run 'automaton login'.");
        }
        return (new AutomatonApiClient(resolved.Server, resolved.ApiKey), resolved.AppId);
    }
}
