using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Apps;

public class AppsViewModel
{
    private readonly AppsService appsService;
    private readonly ResourceAuthorizationService authorizationService;

    public ICollection<AppListItem> Apps { get; set; } = new List<AppListItem>();

    /// <summary>
    /// Check if user can manage resources (update/delete) in the current context.
    /// </summary>
    public bool CanManageApps => authorizationService.CanManageResources();

    public AppsViewModel(AppsService appsService, ResourceAuthorizationService authorizationService)
    {
        this.appsService = appsService;
        this.authorizationService = authorizationService;
    }

    public async Task GetApps()
    {
        Apps = await appsService.List();
    }

    public async Task CreateApp(NewApp app)
    {
        var newApp = await appsService.Create(app);

        Apps.Add(new AppListItem
        {
            Id = newApp.Id,
            Name = newApp.Name,
            DisplayName = newApp.DisplayName,
            IsPublished = newApp.IsPublished,
            PublicUrl = newApp.PublicUrl,
            Created = newApp.Created,
            Updated = newApp.Updated,
            ExecutionRunnerId = newApp.ExecutionRunnerId
        });
    }

    public async Task DeleteApp(Guid id)
    {
        await appsService.Delete(id);

        var app = Apps.FirstOrDefault(a => a.Id == id);
        if (app != null)
        {
            Apps.Remove(app);
        }
    }

    public async Task PublishApp(Guid id, string publicUrl, bool requiresAuthentication = true, Guid? executionRunnerId = null)
    {
        await appsService.PublishAsync(id, publicUrl, requiresAuthentication, executionRunnerId);

        var app = Apps.FirstOrDefault(a => a.Id == id);
        if (app != null)
        {
            app.IsPublished = true;
            app.PublicUrl = publicUrl;
            app.RequiresAuthentication = requiresAuthentication;
        }
    }

    public async Task UnpublishApp(Guid id)
    {
        await appsService.Unpublish(id);

        var app = Apps.FirstOrDefault(a => a.Id == id);
        if (app != null)
        {
            app.IsPublished = false;
            app.PublicUrl = null;
        }
    }
}
