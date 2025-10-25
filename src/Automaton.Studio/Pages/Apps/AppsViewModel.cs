using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Apps;

public class AppsViewModel
{
    private readonly AppsService appsService;

    public ICollection<AppListItem> Apps { get; set; } = new List<AppListItem>();

    public AppsViewModel(AppsService appsService)
    {
        this.appsService = appsService;
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
            Updated = newApp.Updated
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

    public async Task PublishApp(Guid id, string publicUrl, bool requiresAuthentication = true)
    {
        await appsService.PublishAsync(id, publicUrl, requiresAuthentication);

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
