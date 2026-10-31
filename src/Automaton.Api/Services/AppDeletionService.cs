using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;

namespace Automaton.Api.Services;

/// <summary>Removes app storage and database state while releasing claimed runtime deletions on failure.</summary>
public sealed class AppDeletionService(
    ApplicationDbContext dataContext,
    AppFileStorageService fileStorageService,
    AppDeploymentTargetManagementService? deploymentTargetManagementService = null,
    BackendRouteConfigService? routeConfigService = null)
{
    public async Task DeleteResourcesAsync(
        Guid appId, WebApp app, Guid ownerId, bool recoverInterrupted = false, bool abandonOfflineRuntimes = false)
    {
        if (abandonOfflineRuntimes && !recoverInterrupted)
            throw new ArgumentException("Abandoning offline runtimes requires interrupted-deletion recovery.");
        await using var executor = await AppDeletionExecutionLock.AcquireAsync(dataContext, appId);
        IReadOnlyDictionary<Guid, DeploymentTargetStatus>? deletionClaims = null;
        Guid? appClaimId = null;
        var resuming = false;
        var routesRevoked = false;
        var appClaims = new AppDeletionClaimService(dataContext);
        try
        {
            await dataContext.Entry(app).ReloadAsync();
            if (abandonOfflineRuntimes && !app.DeletionClaimId.HasValue)
                throw new AppDeploymentTargetConflictException(
                    "Offline runtime abandonment is available only for an interrupted deletion.");
            resuming = recoverInterrupted && app.DeletionClaimId.HasValue;
            appClaimId = resuming
                ? await appClaims.RecoverAsync(executor)
                : await appClaims.AcquireAsync(appId);
            await dataContext.Entry(app).ReloadAsync();
            await appClaims.AssertOwnedAsync(executor, appClaimId.Value, CancellationToken.None);
            // Revoke edge routing before dispatching destructive runtime or storage cleanup.
            if (routeConfigService is not null)
            {
                await routeConfigService.RefreshAsync();
                routesRevoked = true;
            }
            await appClaims.AssertOwnedAsync(executor, appClaimId.Value, CancellationToken.None);
            if (deploymentTargetManagementService is not null)
            {
                deletionClaims = await deploymentTargetManagementService
                    .ClaimAndCleanupAppRuntimesAsync(appId, recoverInterrupted: resuming,
                        abandonOfflineRuntimes: abandonOfflineRuntimes,
                        assertDeletionOwner: ct => appClaims.AssertOwnedAsync(executor, appClaimId.Value, ct),
                        deletionClaimId: appClaimId.Value);
            }

            await appClaims.AssertOwnedAsync(executor, appClaimId.Value, CancellationToken.None);
            await fileStorageService.DeleteAppAsync(ownerId, appId);
            await appClaims.AssertOwnedAsync(executor, appClaimId.Value, CancellationToken.None);
            await AppSecretService.RemoveAllForAppAsync(dataContext, appId, CancellationToken.None);
            dataContext.WebApps.Remove(app);
            await dataContext.SaveChangesAsync();
        }
        catch (Exception deletionError)
        {
            await CompensateFailureAsync(appId, deletionClaims, appClaimId, resuming, routesRevoked, deletionError);
            throw;
        }
    }

    private async Task CompensateFailureAsync(Guid appId,
        IReadOnlyDictionary<Guid, DeploymentTargetStatus>? deletionClaims, Guid? appClaimId,
        bool resuming, bool routesRevoked, Exception deletionError)
    {
        if (deploymentTargetManagementService is not null && deletionClaims is not null)
        {
            dataContext.ChangeTracker.Clear();
            await deploymentTargetManagementService.ReleaseAppDeletionClaimsAsync(
                appId, deletionClaims, CancellationToken.None, appClaimId);
        }

        // Interrupted recovery keeps its intent closed to new work and traffic.
        if (!appClaimId.HasValue || resuming) return;
        await new AppDeletionClaimService(dataContext).ReleaseAsync(appId, appClaimId.Value, CancellationToken.None);
        if (!routesRevoked || routeConfigService is null) return;
        try { await routeConfigService.RefreshAsync(); }
        catch (Exception restorationError)
        {
            throw new AggregateException("App deletion failed and its routes could not be restored. Resolve the routing error before retrying.",
                deletionError, restorationError);
        }
    }
}
