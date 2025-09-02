using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Serilog;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class CustomStepsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly RecentActivityService recentActivityService;
    private readonly Guid userId;
    private readonly Serilog.ILogger logger;
    private const string DefaultIcon = "user";
    private const string DefaultGroup = "My Steps";

    public CustomStepsService
    (
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService
    )
    {
        this.dataContext = dataContext;
        this.recentActivityService = recentActivityService;
        this.userId = userContextService.GetUserId();
        this.logger = Log.ForContext<CustomStepsService>();
    }

    public IEnumerable<CustomStep> List()
    {
        var entities = dataContext.CustomSteps.Where(x => x.CustomStepUsers.Any(x => x.UserId == userId));
        var steps = entities.Select(MapToModel);

        return steps;
    }

    public CustomStep Get(Guid id)
    {
        var stepEntity = dataContext.CustomSteps.SingleOrDefault(x => x.Id == id && x.CustomStepUsers.Any(x => x.UserId == userId));
        var step = MapToModel(stepEntity);
 
        return step;
    }

    public async Task<Guid> Create(NewCustomStep step)
    { 
        var entity = new Entities.CustomStep
        {
            Id = Guid.NewGuid(),
            Name = step.Name,
            Icon = DefaultIcon,
            Category = DefaultGroup,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DisplayName = string.Empty,
            Description = string.Empty,
            MoreInfo = string.Empty,
            Definition = JsonSerializer.Serialize(new CustomStepDefinition())
        };

        dataContext.CustomSteps.Add(entity);

        var stepUser = new Entities.CustomStepUser
        {
            CustomStepId = entity.Id,
            UserId = userId
        };

        dataContext.CustomStepUsers.Add(stepUser);

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogCustomStepActivityAsync(ActivityType.CustomStepCreated, step.Name, entity.Id);

        return entity.Id;
    }

    public async Task Update(Guid id, CustomStep step)
    {
        var entity = dataContext.CustomSteps.SingleOrDefault(x => x.Id == id && x.CustomStepUsers.Any(x => x.UserId == userId));

        entity.Name = step.Name;
        entity.DisplayName = step.DisplayName;
        entity.Description = step.Description;
        entity.Icon = step.Icon;
        entity.Category = step.Category;
        entity.MoreInfo = step.MoreInfo;
        entity.VisibleInExplorer = step.VisibleInExplorer;
        entity.Updated = DateTime.UtcNow;
        entity.Definition = JsonSerializer.Serialize(step.Definition);

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogCustomStepActivityAsync(ActivityType.CustomStepUpdated, step.Name, id);
    }

    public async Task Remove(Guid id)
    {
        var customStep = dataContext.CustomSteps.SingleOrDefault(x => x.Id == id && x.CustomStepUsers.Any(x => x.UserId == userId));
        var stepName = customStep.Name;

        dataContext.CustomSteps.Remove(customStep);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogCustomStepActivityAsync(ActivityType.CustomStepDeleted, stepName, id);
    }

    public bool Exists(string name)
    {
        var exists = dataContext.CustomSteps.Any(x => x.Name == name && x.CustomStepUsers.Any(x => x.UserId == userId));

        return exists;
    }

    private static CustomStep MapToModel(Entities.CustomStep entity)
    {
        if (entity == null) return null;

        return new CustomStep
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Icon = entity.Icon,
            Category = entity.Category,
            MoreInfo = entity.MoreInfo,
            VisibleInExplorer = entity.VisibleInExplorer,
            Created = entity.Created,
            Updated = entity.Updated,
            Definition = JsonSerializer.Deserialize<CustomStepDefinition>(entity.Definition ?? "{}")
        };
    }
}
