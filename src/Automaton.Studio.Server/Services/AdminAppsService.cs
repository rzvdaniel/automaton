using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class AdminAppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly AppFileStorageService fileStorageService;
    private readonly AdminAuthorizationService adminAuthorizationService;

    public AdminAppsService(
        ApplicationDbContext dataContext,
        ResourceAuthorizationService authorizationService,
        AppFileStorageService fileStorageService,
        AdminAuthorizationService adminAuthorizationService)
    {
        this.dataContext = dataContext;
        this.authorizationService = authorizationService;
        this.fileStorageService = fileStorageService;
        this.adminAuthorizationService = adminAuthorizationService;
    }

    public IEnumerable<Models.AppModel> ListOwnedByUserForAdmin(Guid userId, Guid? projectId = null)
    {
        EnsureAdminAccess();

        var entities = authorizationService.GetAppsQueryByContext(projectId, userId)
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots);

        return entities
            .AsEnumerable()
            .Select(entity =>
            {
                var app = AppsService.MapToModel(entity, null);
                var ownerId = entity.WebAppUsers?.FirstOrDefault()?.UserId ?? userId;
                AppsService.ApplyStorageMetadata(app, fileStorageService.GetAppStorageMetadataAsync(ownerId, entity.Id).Result);
                return app;
            });
    }

    private void EnsureAdminAccess() => adminAuthorizationService.EnsureAdmin();
}
