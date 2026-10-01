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
        CancellationToken cancellationToken,
        Guid? deploymentOperationId = null)
    {
        await connectedFlowArtifactService.SynchronizeAsync(
            appId,
            deploymentTargetId,
            ownerId,
            [runner],
            cancellationToken,
            deploymentOperationId);
        await DeployPackageContractService.PersistAsync(dataContext, appId, packageApplication, cancellationToken);
        await PersistSourceAsync(appId, ownerId, file, cancellationToken);
        await fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Local;
            metadata.SourceModifiedAtUtc = DateTime.UtcNow;
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
        var sourceRoot = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        await using var stream = file.OpenReadStream();
        await AppSourceArchiveInstaller.InstallAsync(stream, sourceRoot, cancellationToken);
    }
}
