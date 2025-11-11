using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Server.Services;

public class AppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly RecentActivityService recentActivityService;
    private readonly UserContextService userContextService;
    private readonly Serilog.ILogger logger;

    // ✅ Helper property to get current userId (respects impersonation)
    private Guid UserId => userContextService.GetUserId();

    public AppsService(
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService
    )
    {
        this.dataContext = dataContext;
        this.userContextService = userContextService;
        this.recentActivityService = recentActivityService;
        this.logger = Serilog.Log.ForContext<AppsService>();
    }

    public IEnumerable<Models.AppModel> List()
    {
        var entities = dataContext.WebApps.Where(x => x.WebAppUsers.Any(x => x.UserId == UserId));
        var apps = entities.Select(MapToModel);

        return apps;
    }

    public Models.AppModel Get(Guid id)
    {
        var appEntity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == UserId));
        var app = MapToModel(appEntity);

        return app;
    }

    /// <summary>
    /// Get app by public URL slug. Validates published status and ownership.
    /// Returns app only if it's published and owned by the current user (respects impersonation).
    /// </summary>
    public Models.AppModel GetBySlug(string slug)
    {
        var appEntity = dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefault(x =>
                x.PublicUrl != null &&
                x.PublicUrl.ToLower() == slug.ToLower() &&
                x.IsPublished &&
                x.WebAppUsers.Any(u => u.UserId == UserId));

        return MapToModel(appEntity);
    }

    /// <summary>
    /// Get published app metadata by slug for access validation.
    /// Returns app info without requiring ownership (for authentication checks).
    /// Used by both AppsController and WebAppFlowExecutionController.
    /// </summary>
    public async Task<Models.PublishedAppInfo> GetPublishedAppInfoAsync(string slug, CancellationToken cancellationToken = default)
    {
        var appEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x =>
                x.PublicUrl != null &&
                x.PublicUrl.ToLower() == slug.ToLower() &&
                x.IsPublished,
                cancellationToken);

        if (appEntity == null)
        {
            return null;
        }

        var ownerId = appEntity.WebAppUsers.FirstOrDefault()?.UserId;

        return new Models.PublishedAppInfo
        {
            Id = appEntity.Id,
            Name = appEntity.Name,
            PublicUrl = appEntity.PublicUrl,
            RequiresAuthentication = appEntity.RequiresAuthentication,
            OwnerId = ownerId,
            ExecutionRunnerId = appEntity.ExecutionRunnerId
        };
    }

    public async Task<Guid> Create(Models.NewApp app)
    {
        // If an execution runner id was provided, ensure it belongs to the owner (current user)
        if (app.ExecutionRunnerId.HasValue)
        {
            var runnerExists = dataContext.Runners
                .Any(r => r.Id == app.ExecutionRunnerId.Value && r.RunnerUsers.Any(u => u.UserId == UserId));

            if (!runnerExists)
            {
                throw new InvalidOperationException("Execution runner not found or you do not have access to it.");
            }
        }

        var entity = new Entities.WebApp
        {
            Id = Guid.NewGuid(),
            Name = app.Name,
            DisplayName = string.Empty,
            Description = string.Empty,
            IsPublished = false,
            PublicUrl = null,
            PublishedAt = null,
            Version = 1,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            Definition = JsonSerializer.Serialize(new Models.AppDefinition()),
            ExecutionRunnerId = app.ExecutionRunnerId
        };

        dataContext.WebApps.Add(entity);

        var appUser = new Entities.WebAppUser
        {
            WebAppId = entity.Id,
            UserId = UserId
        };

        dataContext.WebAppUsers.Add(appUser);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, app.Name, entity.Id);

        return entity.Id;
    }

    public async Task Update(Guid id, Models.AppModel app)
    {
        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == UserId));

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
        
        // Persist appearance configuration as JSON
        entity.Appearance = JsonSerializer.Serialize(app.Appearance);

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUpdated, app.Name, id);
    }

    public async Task Remove(Guid id)
    {
        var app = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == UserId));

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

    public async Task Publish(Guid id, string publicUrl, bool requiresAuthentication = true, Guid? executionRunnerId = null)
    {
        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == UserId));

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

        // If an execution runner id was provided, ensure it belongs to the owner
        if (executionRunnerId.HasValue)
        {
            var runnerExists = dataContext.Runners
                .Any(r => r.Id == executionRunnerId.Value && r.RunnerUsers.Any(u => u.UserId == UserId));

            if (!runnerExists)
            {
                throw new InvalidOperationException("Execution runner not found or you do not have access to it.");
            }
        }

        entity.IsPublished = true;
        entity.PublicUrl = publicUrl;
        entity.PublishedAt = DateTime.UtcNow;
        entity.RequiresAuthentication = requiresAuthentication;
        entity.ExecutionRunnerId = executionRunnerId;
        entity.Version++;
        entity.Updated = DateTime.UtcNow;

        dataContext.SaveChanges();

        logger.Information("App {AppId} published with slug {Slug} (v{Version}, Auth: {RequiresAuth}, Runner: {Runner})",
            id, publicUrl, entity.Version, requiresAuthentication, executionRunnerId);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppPublished, entity.Name, id);
    }

    public async Task Unpublish(Guid id)
    {
        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id && x.WebAppUsers.Any(x => x.UserId == UserId));

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        entity.IsPublished = false;
        entity.PublishedAt = null;
        entity.Updated = DateTime.UtcNow;
        // Keep PublicUrl for re-publishing with same slug

        dataContext.SaveChanges();

        logger.Information("App {AppId} unpublished (v{Version})", id, entity.Version);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUnpublished, entity.Name, id);
    }

    /// <summary>
    /// Generate a unique URL slug from display name
    /// </summary>
    public string GenerateUniqueSlug(string displayName)
    {
        // Convert to lowercase and replace spaces with hyphens
        var slug = displayName.ToLowerInvariant()
            .Replace(' ', '-')
    .Replace('_', '-');

        // Remove special characters (keep only alphanumeric and hyphens)
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

        // Remove multiple consecutive hyphens
        slug = Regex.Replace(slug, @"\-+", "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-');

        // Ensure uniqueness
        var originalSlug = slug;
        var counter = 1;
        while (dataContext.WebApps.Any(x => x.PublicUrl == slug))
        {
            slug = $"{originalSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    private static bool IsValidUrlSlug(string slug)
    {
        // URL slug validation: lowercase letters, numbers, hyphens
        return !string.IsNullOrEmpty(slug) && Regex.IsMatch(slug, @"^[a-z0-9\-]+$");
    }

    private static Models.AppModel MapToModel(Entities.WebApp entity)
    {
        if (entity == null) return null;

        var appearance = string.IsNullOrEmpty(entity.Appearance)
            ? new Models.AppAppearance()
            : JsonSerializer.Deserialize<Models.AppAppearance>(entity.Appearance) ?? new Models.AppAppearance();

        return new Models.AppModel
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            IsPublished = entity.IsPublished,
            PublicUrl = entity.PublicUrl,
            PublishedAt = entity.PublishedAt,
            Version = entity.Version,
            RequiresAuthentication = entity.RequiresAuthentication,
            Created = entity.Created,
            Updated = entity.Updated,
            ExecutionRunnerId = entity.ExecutionRunnerId,
            Appearance = appearance,
            Definition = JsonSerializer.Deserialize<Models.AppDefinition>(entity.Definition ?? "{}") ?? new Models.AppDefinition()
        };
    }
}
