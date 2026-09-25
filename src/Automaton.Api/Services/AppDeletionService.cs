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
    public async Task DeleteResourcesAsync(Guid appId, WebApp app, Guid ownerId)
    {
        IReadOnlyDictionary<Guid, DeploymentTargetStatus>? deletionClaims = null;
        try
        {
            if (deploymentTargetManagementService is not null)
            {
                deletionClaims = await deploymentTargetManagementService
                    .ClaimAndCleanupAppRuntimesAsync(appId);
            }

            await fileStorageService.DeleteAppAsync(ownerId, appId);
            dataContext.WebApps.Remove(app);
            await dataContext.SaveChangesAsync();
        }
        catch
        {
            if (deploymentTargetManagementService is not null && deletionClaims is not null)
            {
                dataContext.ChangeTracker.Clear();
                await deploymentTargetManagementService.ReleaseAppDeletionClaimsAsync(
                    appId, deletionClaims, CancellationToken.None);
            }

            throw;
        }
    }
}
