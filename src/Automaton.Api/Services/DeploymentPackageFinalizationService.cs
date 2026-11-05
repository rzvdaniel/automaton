using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Constants;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Applies API-side package state after the Runner reports a successful deployment.
/// </summary>
public sealed class DeploymentPackageFinalizationService(
    ApplicationDbContext dataContext,
    AppFileStorageService fileStorageService,
    ConnectedFlowArtifactService connectedFlowArtifactService,
    BackendRouteConfigService backendRouteConfigService,
    MachineStaticAppPublicationService? staticPublication = null)
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
        // Source and managed artifacts belong to the project owner for project apps, where Studio deploy
        // reads them even when a project editor ran the deploy (forking still reads the first linked user).
        var sourceOwnerId = await ResolveSourceOwnerIdAsync(appId, ownerId, cancellationToken);
        await connectedFlowArtifactService.SynchronizeAsync(
            appId,
            deploymentTargetId,
            sourceOwnerId,
            [runner],
            cancellationToken,
            deploymentOperationId);
        await DeployPackageContractService.PersistAsync(dataContext, appId, packageApplication, cancellationToken);
        await PersistSourceAsync(appId, sourceOwnerId, file, cancellationToken);
        await fileStorageService.UpdateAppStorageMetadataAsync(sourceOwnerId, appId, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Local;
            metadata.SourceModifiedAtUtc = DateTime.UtcNow;
        });
        await backendRouteConfigService.RefreshAsync(
            cancellationToken,
            deploymentTargetId,
            packageApplication.Runtime.Command is not null
                || packageApplication.Compose is not null || packageApplication.IsStaticSite);
        await RepublishStaticSiteAsync(appId, deploymentTargetId, ownerId, packageApplication, cancellationToken);
    }

    // Moving the Live environment to another Runner removes the published copy from the old Runner.
    // Republishing after each Live deploy keeps a public static site served from its current Runner.
    private async Task RepublishStaticSiteAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid ownerId,
        ApplicationContract packageApplication,
        CancellationToken cancellationToken)
    {
        if (staticPublication is null || !packageApplication.IsStaticSite) return;
        var isLive = await dataContext.AppDeploymentTargets
            .AnyAsync(target => target.Id == deploymentTargetId && target.IsDefault, cancellationToken);
        if (!isLive) return;
        var app = await dataContext.WebApps.SingleAsync(app => app.Id == appId, cancellationToken);
        await staticPublication.PublishIfAssignedAsync(app, app.RuntimeVisibility, ownerId, cancellationToken);
    }

    /// <summary>
    /// Stores the package's secret declarations before the Runner starts the new runtime, which loads
    /// secrets while the rest of the contract is persisted only after the deployment succeeds.
    /// </summary>
    public async Task PersistSecretDeclarationsAsync(Guid appId, ApplicationContract packageApplication, CancellationToken cancellationToken)
    {
        var app = await dataContext.WebApps.SingleOrDefaultAsync(item => item.Id == appId, cancellationToken);
        if (app is null) return;
        ApplicationContract stored;
        try { stored = ApplicationContractSerialization.Deserialize(app.ApplicationContractJson); }
        // A stored contract that no longer reads stays untouched; the deployment finalizes a new one.
        catch (InvalidOperationException) { return; }
        catch (System.Text.Json.JsonException) { return; }
        var declarations = packageApplication.Secrets ?? [];
        var permissions = packageApplication.Permissions ?? [];
        var settings = packageApplication.Settings ?? [];
        var schedules = packageApplication.Schedules ?? [];
        if (System.Text.Json.JsonSerializer.Serialize(stored.Secrets) == System.Text.Json.JsonSerializer.Serialize(declarations)
            && System.Text.Json.JsonSerializer.Serialize(stored.Permissions) == System.Text.Json.JsonSerializer.Serialize(permissions)
            && System.Text.Json.JsonSerializer.Serialize(stored.Settings) == System.Text.Json.JsonSerializer.Serialize(settings)
            && System.Text.Json.JsonSerializer.Serialize(stored.Schedules) == System.Text.Json.JsonSerializer.Serialize(schedules)) return;
        stored.Secrets = declarations;
        stored.Permissions = permissions;
        stored.Settings = settings;
        stored.Schedules = schedules;
        var removedApprovals = await dataContext.AppPermissionApprovals
            .Where(approval => approval.AppId == appId && !permissions.Contains(approval.Permission))
            .ToListAsync(cancellationToken);
        foreach (var approval in removedApprovals)
        {
            if (approval.ProtectedKey is not null || approval.Status == AppPermissionStatuses.Approved)
                approval.Status = AppPermissionStatuses.Revoked;
            approval.ProtectedKey = null;
            approval.TokenHash = null;
            approval.UpdatedAtUtc = DateTime.UtcNow;
        }
        app.ApplicationContractJson = ApplicationContractSerialization.Serialize(stored);
        await dataContext.SaveChangesAsync(cancellationToken);
    }

    public Task RestoreRoutesAsync() => backendRouteConfigService.RefreshAsync(CancellationToken.None);

    internal async Task<Guid> ResolveSourceOwnerIdAsync(Guid appId, Guid actingUserId, CancellationToken cancellationToken)
    {
        var projectOwnerId = await dataContext.WebApps
            .Where(app => app.Id == appId && app.ProjectId != null)
            .Join(dataContext.Projects, app => app.ProjectId, project => project.Id, (app, project) => (Guid?)project.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);
        return projectOwnerId ?? actingUserId;
    }

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
