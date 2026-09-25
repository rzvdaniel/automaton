using System.IO.Compression;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Constants;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

/// <summary>
/// Applies API-side package state after the Runner reports a successful deployment.
/// </summary>
public sealed class DeploymentPackageFinalizationService(
    ApplicationDbContext dataContext,
    AppFileStorageService fileStorageService,
    ConnectedFlowArtifactService connectedFlowArtifactService,
    BackendRouteConfigService backendRouteConfigService)
{
    public async Task FinalizeAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid ownerId,
        Runner runner,
        IFormFile file,
        ApplicationContract packageApplication,
        CancellationToken cancellationToken)
    {
        await connectedFlowArtifactService.SynchronizeAsync(
            appId,
            deploymentTargetId,
            ownerId,
            [runner],
            cancellationToken);
        await DeployPackageContractService.PersistAsync(dataContext, appId, packageApplication, cancellationToken);
        await PersistSourceAsync(appId, ownerId, file, cancellationToken);
        await fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Local;
        });
        await backendRouteConfigService.RefreshAsync(
            cancellationToken,
            deploymentTargetId,
            packageApplication.Runtime.Command is not null);
    }

    public Task RestoreRoutesAsync() => backendRouteConfigService.RefreshAsync(CancellationToken.None);

    private async Task PersistSourceAsync(
        Guid appId,
        Guid ownerId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await fileStorageService.ClearSourceAsync(ownerId, appId);
        var sourceRoot = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        Directory.CreateDirectory(sourceRoot);

        using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var normalizedPath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (!normalizedPath.StartsWith("source/", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativeSourcePath = normalizedPath["source/".Length..];
            if (string.IsNullOrWhiteSpace(relativeSourcePath))
                continue;

            var destinationPath = Path.GetFullPath(Path.Combine(sourceRoot, relativeSourcePath));
            var fullSourceRoot = Path.GetFullPath(sourceRoot);
            if (!destinationPath.StartsWith(fullSourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Deploy package source entry '{relativeSourcePath}' escapes the app source directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(destinationPath);
            await entryStream.CopyToAsync(outputStream, cancellationToken);
        }
    }
}
