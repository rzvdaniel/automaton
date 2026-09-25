using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

public sealed class AppImageManagementService(
    AppsService appsService,
    IWebHostEnvironment environment,
    ILogger<AppImageManagementService> logger)
{
    private const long MaxFileSize = 2 * 1024 * 1024;
    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    public async Task<AppImageOperationResult> UploadAsync(Guid appId, IFormFile? file, string? userName)
    {
        try
        {
            if (appsService.Get(appId) is null)
            {
                logger.LogWarning("Thumbnail upload attempt by user {User} to app not owned: {AppId}", userName ?? "Unknown", appId);
                return AppImageOperationResult.NotFound();
            }

            var validationError = Validate(file, appId);
            if (validationError is not null)
            {
                return AppImageOperationResult.BadRequest(validationError);
            }

            var validatedFile = file!;
            var extension = Path.GetExtension(validatedFile.FileName).ToLowerInvariant();
            var directory = GetAppMediaDirectory(appId);
            await DeleteExistingImagesAsync(directory);
            Directory.CreateDirectory(directory);

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(directory, fileName);
            if (!IsWithinDirectory(filePath, directory))
            {
                logger.LogError("Thumbnail path traversal attempt detected for app {AppId}", appId);
                return AppImageOperationResult.BadRequest("Invalid file path");
            }

            if (!await SaveValidatedImageAsync(validatedFile, filePath, extension, appId))
            {
                return AppImageOperationResult.BadRequest("File content is not a valid image");
            }

            var imageUrl = $"/app-thumbnails/{appId}/{fileName}";
            await appsService.UpdateThumbnailUrlAsync(appId, imageUrl);
            logger.LogDebug("Thumbnail uploaded for app {AppId}: {FileName} by user {User}", appId, fileName, userName);
            return AppImageOperationResult.Success(imageUrl);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error uploading background image for app {AppId}", appId);
            return AppImageOperationResult.Failure();
        }
    }

    public async Task<AppImageOperationResult> UploadScreenshotAsync(Guid appId, IFormFile? file, string? userName)
    {
        try
        {
            var app = appsService.Get(appId);
            if (app is null)
            {
                logger.LogWarning("Screenshot upload attempt by user {User} to app not owned: {AppId}", userName ?? "Unknown", appId);
                return AppImageOperationResult.NotFound();
            }

            if (file is null || file.Length == 0)
            {
                return AppImageOperationResult.BadRequest("No file provided");
            }

            if ((app.Screenshots?.Count ?? 0) >= 8)
            {
                return AppImageOperationResult.BadRequest("You can upload up to 8 screenshots per app");
            }

            var validationError = Validate(file, appId);
            if (validationError is not null)
            {
                return AppImageOperationResult.BadRequest(validationError);
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var directory = GetAppMediaDirectory(appId, "app-screenshots");
            Directory.CreateDirectory(directory);
            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(directory, fileName);
            if (!IsWithinDirectory(filePath, directory))
            {
                logger.LogError("Screenshot path traversal attempt detected for app {AppId}", appId);
                return AppImageOperationResult.BadRequest("Invalid file path");
            }

            if (!await SaveValidatedImageAsync(file, filePath, extension, appId))
            {
                return AppImageOperationResult.BadRequest("File content is not a valid image");
            }

            var imageUrl = $"/app-screenshots/{appId}/{fileName}";
            var screenshot = await appsService.AddScreenshotAsync(appId, imageUrl);
            logger.LogDebug("Screenshot uploaded for app {AppId}: {FileName} by user {User}", appId, fileName, userName);
            return AppImageOperationResult.SuccessPayload(screenshot);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error uploading screenshot for app {AppId}", appId);
            return AppImageOperationResult.Failure("Failed to upload screenshot");
        }
    }

    public async Task<AppImageOperationResult> DeleteScreenshotAsync(Guid appId, Guid screenshotId, string? userName)
    {
        try
        {
            if (appsService.Get(appId) is null)
            {
                logger.LogWarning("Screenshot delete attempt by user {User} to app not owned: {AppId}", userName ?? "Unknown", appId);
                return AppImageOperationResult.NotFound();
            }

            var imageUrl = await appsService.DeleteScreenshotAsync(appId, screenshotId);
            DeleteImageFromUrl(imageUrl, GetAppMediaDirectory(appId, "app-screenshots"));
            return AppImageOperationResult.NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return AppImageOperationResult.NotFound(exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error deleting screenshot {ScreenshotId} for app {AppId}", screenshotId, appId);
            return AppImageOperationResult.Failure("Failed to delete screenshot");
        }
    }

    private string? Validate(IFormFile? file, Guid appId)
    {
        if (file is null || file.Length == 0)
        {
            return "No file provided";
        }

        if (file.Length > MaxFileSize)
        {
            logger.LogWarning("Thumbnail file size exceeds limit for app {AppId}: {Size} bytes", appId, file.Length);
            return "File size exceeds 2MB limit";
        }

        if (!AllowedMimeTypes.Contains(file.ContentType))
        {
            logger.LogWarning("Invalid thumbnail MIME type for app {AppId}: {MimeType}", appId, file.ContentType);
            return "Invalid file type. Only image files are allowed.";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            logger.LogWarning("Invalid thumbnail extension for app {AppId}: {Extension}", appId, extension);
            return "Invalid file extension. Only .jpg, .png, .gif, and .webp are allowed.";
        }

        if (Path.GetFileName(file.FileName) != file.FileName || file.FileName.Contains(".."))
        {
            logger.LogWarning("Suspicious thumbnail filename detected for app {AppId}: {FileName}", appId, file.FileName);
            return "Invalid filename";
        }

        return null;
    }

    private async Task<bool> SaveValidatedImageAsync(IFormFile file, string filePath, string extension, Guid appId)
    {
        await using var stream = file.OpenReadStream();
        if (!ImageSignatureValidator.IsValid(stream, extension))
        {
            logger.LogWarning("Thumbnail content doesn't match image format for app {AppId}", appId);
            return false;
        }

        stream.Seek(0, SeekOrigin.Begin);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
        return true;
    }

    private async Task DeleteExistingImagesAsync(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in new DirectoryInfo(directory).GetFiles("*.*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    file.Delete();
                    logger.LogDebug("Deleted old thumbnail image: {FilePath}", file.FullName);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to delete old thumbnail image: {FilePath}", file.FullName);
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error cleaning up old thumbnails in {Directory}", directory);
        }
    }

    private string GetAppMediaDirectory(Guid appId, string mediaFolder = "app-thumbnails")
        => Path.Combine(environment.ContentRootPath, "AppData", "media", mediaFolder, appId.ToString());

    private void DeleteImageFromUrl(string? imageUrl, string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return;

        try
        {
            var fileName = Path.GetFileName(imageUrl);
            if (string.IsNullOrWhiteSpace(fileName)) return;
            var filePath = Path.Combine(parentDirectory, fileName);
            if (!IsWithinDirectory(filePath, parentDirectory))
            {
                logger.LogWarning("Skipped deleting image outside allowed directory: {ImageUrl}", imageUrl);
                return;
            }

            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to delete image for URL {ImageUrl}", imageUrl);
        }
    }

    private static bool IsWithinDirectory(string filePath, string directory)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fullDirectory = Path.GetFullPath(directory);
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}

public sealed record AppImageOperationResult(
    int StatusCode,
    string? ImageUrl = null,
    string? ErrorMessage = null,
    object? Payload = null)
{
    public static AppImageOperationResult Success(string imageUrl) => new(StatusCodes.Status200OK, imageUrl);
    public static AppImageOperationResult SuccessPayload(object payload) => new(StatusCodes.Status200OK, Payload: payload);
    public static AppImageOperationResult NoContent() => new(StatusCodes.Status204NoContent);
    public static AppImageOperationResult NotFound(string? error = null) => new(StatusCodes.Status404NotFound, ErrorMessage: error);
    public static AppImageOperationResult BadRequest(string error) => new(StatusCodes.Status400BadRequest, ErrorMessage: error);
    public static AppImageOperationResult Failure(string error = "Failed to upload image") => new(StatusCodes.Status500InternalServerError, ErrorMessage: error);
}

internal static class ImageSignatureValidator
{
    public static bool IsValid(Stream stream, string extension)
    {
        Span<byte> buffer = stackalloc byte[12];
        var bytesRead = stream.Read(buffer);
        if (bytesRead < 4) return false;

        return MatchesImageSignature(extension, buffer, bytesRead);
    }

    private static bool MatchesImageSignature(string extension, Span<byte> buffer, int bytesRead)
        => extension.ToLowerInvariant() switch
        {
            ".png" => buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47,
            ".jpg" or ".jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF,
            ".gif" => buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46,
            ".webp" => HasWebpSignature(buffer, bytesRead),
            _ => false
        };

    private static bool HasWebpSignature(Span<byte> buffer, int bytesRead)
        => bytesRead >= 12
           && buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46
           && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50;
}
