using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Server.Services;

public class AppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly RecentActivityService recentActivityService;
    private readonly Guid userId;
    private readonly Serilog.ILogger logger;

    public AppsService(
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService
    )
    {
        this.dataContext = dataContext;
        this.recentActivityService = recentActivityService;
        this.userId = userContextService.GetUserId();
        this.logger = Serilog.Log.ForContext<AppsService>();
    }

    public IEnumerable<Models.AppModel> List()
    {
        var entities = dataContext.WebApps.Where(x => x.WebAppUsers.Any(x => x.UserId == userId));
        var apps = entities.Select(MapToModel);

        return apps;
    }

    public Models.AppModel Get(Guid id)
    {
        var appEntity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == userId));
        var app = MapToModel(appEntity);

        return app;
    }

    public async Task<Guid> Create(Models.NewApp app)
    {
        var entity = new Entities.WebApp
        {
            Id = Guid.NewGuid(),
            Name = app.Name,
            DisplayName = string.Empty,
            Description = string.Empty,
            IsPublished = false,
            PublicUrl = null,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            Definition = JsonSerializer.Serialize(new Models.AppDefinition())
        };

        dataContext.WebApps.Add(entity);

        var appUser = new Entities.WebAppUser
        {
            WebAppId = entity.Id,
            UserId = userId
        };

        dataContext.WebAppUsers.Add(appUser);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, app.Name, entity.Id);

        return entity.Id;
    }

    public async Task Update(Guid id, Models.AppModel app)
    {
        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == userId));

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        entity.Name = app.Name;
        entity.DisplayName = app.DisplayName;
        entity.Description = app.Description;
        entity.IsPublished = app.IsPublished;
        entity.PublicUrl = app.PublicUrl;
        entity.Updated = DateTime.UtcNow;
        entity.Definition = JsonSerializer.Serialize(app.Definition);

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUpdated, app.Name, id);
    }

    public async Task Remove(Guid id)
    {
        var app = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == userId));
        
        if (app == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        var appName = app.Name;

        dataContext.WebApps.Remove(app);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppDeleted, appName, id);
    }

    public async Task Publish(Guid id, string publicUrl)
    {
        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == userId));

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        // Validate URL slug format
        if (!IsValidUrlSlug(publicUrl))
        {
            throw new InvalidOperationException("Invalid URL slug format. Use lowercase letters, numbers, and hyphens only.");
        }

        // Check if URL is already taken
        if (dataContext.WebApps.Any(x => x.PublicUrl == publicUrl && x.Id != id))
        {
            throw new InvalidOperationException("This URL is already taken");
        }

        entity.IsPublished = true;
        entity.PublicUrl = publicUrl;
        entity.Updated = DateTime.UtcNow;

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppPublished, entity.Name, id);
    }

    public async Task Unpublish(Guid id)
    {
        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == userId));

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        entity.IsPublished = false;
        entity.PublicUrl = null;
        entity.Updated = DateTime.UtcNow;

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUnpublished, entity.Name, id);
    }

    private static bool IsValidUrlSlug(string slug)
    {
        // URL slug validation: lowercase letters, numbers, hyphens
        return !string.IsNullOrEmpty(slug) && Regex.IsMatch(slug, @"^[a-z0-9\-]+$");
    }

    private static Models.AppModel MapToModel(Entities.WebApp entity)
    {
        if (entity == null) return null;

        return new Models.AppModel
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            IsPublished = entity.IsPublished,
            PublicUrl = entity.PublicUrl,
            Created = entity.Created,
            Updated = entity.Updated,
            Definition = JsonSerializer.Deserialize<Models.AppDefinition>(entity.Definition ?? "{}") ?? new Models.AppDefinition()
        };
    }
}
