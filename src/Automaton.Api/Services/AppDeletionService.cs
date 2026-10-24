using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;

namespace Automaton.Api.Services;

/// <summary>Removes app storage and database state while releasing claimed runtime deletions on failure.</summary>
public sealed class AppDeletionService(
    ApplicationDbContext dataContext,
    AppFileStorageService fileStorageService,
    AppDeploymentTargetManagementService? deploymentTargetManagementService = null)
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
            dataContext.WebApps.Remove(app);
            await dataContext.SaveChangesAsync();
        }
        catch
        {
            if (deploymentTargetManagementService is not null && deletionClaims is not null)
            {
                dataContext.ChangeTracker.Clear();
                await deploymentTargetManagementService.ReleaseAppDeletionClaimsAsync(
                    appId, deletionClaims, CancellationToken.None, appClaimId);
            }

            // Recovery remains a pending deletion intent on failure; admission must
            // not reopen while older idempotent cleanup commands can still finish.
            if (appClaimId.HasValue && !resuming)
                await appClaims.ReleaseAsync(appId, appClaimId.Value, CancellationToken.None);
            throw;
        }
    }
}
