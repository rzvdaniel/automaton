using Automaton.Api.Constants;
using Automaton.Api.Enums;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

public sealed class AppRuntimeArchiveUploadService(
    AppsService appsService,
    UserContextService userContextService,
    AppFileStorageService fileStorageService,
    AppVersionService appVersionService,
    ILogger<AppRuntimeArchiveUploadService> logger)
{
    public async Task<RuntimeArchiveUploadResult?> UploadAsync(
        Guid appId,
        IFormFile file,
        bool clean,
        CancellationToken cancellationToken = default)
    {
        var appInfo = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);
        if (appInfo is null)
        {
            return null;
        }

        var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
        var runtimePath = fileStorageService.GetAppRuntimeDirectoryPath(ownerId, appId);
        await using var stream = file.OpenReadStream();
        var result = await AppRuntimeArchiveInstaller.InstallAsync(
            stream, runtimePath, clean, cancellationToken);

        logger.LogDebug(
            "Zip upload for app {AppId}: {Written} written, {Skipped} skipped, {Errors} errors (clean={Clean})",
            appId,
            result.Written.Count,
            result.Skipped.Count,
            result.Errors.Count,
            clean);

        if (result.Errors.Count == 0)
        {
            await appsService.SetLocalDevelopmentMetadataAsync(appId, ownerId, runtimeSource: AppRuntimeSources.CliBuild);
            await TryCreateVersionSnapshotAsync(appId, result.Written.Count);
        }

        return result;
    }

    private async Task TryCreateVersionSnapshotAsync(Guid appId, int writtenCount)
    {
        if (writtenCount == 0)
        {
            return;
        }

        try
        {
            await appVersionService.CreateSnapshotAsync(
                appId,
                userContextService.GetUserId(),
                AppVersionTrigger.ManualSave);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Version snapshot failed for app {AppId} after zip upload — upload still succeeded", appId);
        }
    }
}

public sealed record RuntimeArchiveUploadResult(
    List<string> Written,
    List<object> Skipped,
    List<object> Errors);
