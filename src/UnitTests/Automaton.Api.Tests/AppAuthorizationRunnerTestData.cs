using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class AppAuthorizationRunnerTestData
{
    public static void SeedManagedRunner(ApplicationDbContext dbContext, Guid runnerId, Guid userId, string name = "managed-runner") =>
        SeedRunner(dbContext, runnerId, userId, name, RunnerPurposes.Managed, isOnline: false);

    public static void SeedInfrastructureRunner(
        ApplicationDbContext dbContext,
        Guid runnerId,
        Guid userId,
        string? appTags = null)
    {
        SeedRunner(dbContext, runnerId, userId, "infrastructure-runner", "Infrastructure", isOnline: true);
        SetAppTags(dbContext, appTags);
    }

    public static void SeedWorkflowOnlyRunner(ApplicationDbContext dbContext, Guid runnerId, Guid userId, string appTags)
    {
        var runner = CreateRunner(runnerId, "workflow-only-runner", RunnerPurposes.Managed, isOnline: true);
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(
            [RunnerCapabilities.WorkflowExecution],
            RunnerPurposes.Managed);
        dbContext.Runners.Add(runner);
        dbContext.RunnerUsers.Add(new RunnerUser { RunnerId = runnerId, UserId = userId });
        SetAppTags(dbContext, appTags);
        dbContext.SaveChanges();
    }

    public static void SeedUnassignedManagedRunner(ApplicationDbContext dbContext, Guid runnerId, Guid assignedUserId)
    {
        var runner = CreateRunner(runnerId, "other-runner", RunnerPurposes.Managed, isOnline: false);
        dbContext.Runners.Add(runner);
        dbContext.RunnerUsers.Add(new RunnerUser { RunnerId = runnerId, UserId = assignedUserId });
        dbContext.SaveChanges();
    }

    public static void SetAppTags(ApplicationDbContext dbContext, string? tags)
    {
        if (tags is null)
        {
            return;
        }

        dbContext.WebApps.Single().Tags = tags;
        dbContext.SaveChanges();
    }

    public static async Task<IReadOnlyList<Guid>> GetRunnerIdsVisibleToProjectAsync(
        ApplicationDbContext dbContext,
        Guid projectAId,
        Guid projectBId,
        Guid userId,
        Guid runnerId)
    {
        dbContext.Projects.AddRange(
            AppAuthorizationTestData.CreateProject(projectAId, userId, "project-a"),
            AppAuthorizationTestData.CreateProject(projectBId, userId, "project-b"));
        dbContext.ProjectMembers.AddRange(
            CreateProjectMember(projectAId, userId),
            CreateProjectMember(projectBId, userId));
        dbContext.Runners.Add(CreateRunner(runnerId, "project-b-runner", RunnerPurposes.Managed, isOnline: false));
        dbContext.ProjectRunners.Add(new ProjectRunner
        {
            ProjectId = projectBId,
            RunnerId = runnerId,
            AddedAt = DateTime.UtcNow,
            AddedBy = userId
        });
        await dbContext.SaveChangesAsync();

        return await new ResourceAuthorizationService(dbContext, AppAuthorizationTestData.CreateUserContextService(userId))
            .GetRunnersQueryByContext(projectAId, userId)
            .Select(item => item.Id)
            .ToListAsync();
    }

    private static void SeedRunner(
        ApplicationDbContext dbContext,
        Guid runnerId,
        Guid userId,
        string name,
        string purpose,
        bool isOnline)
    {
        dbContext.Runners.Add(CreateRunner(runnerId, name, purpose, isOnline));
        dbContext.RunnerUsers.Add(new RunnerUser { RunnerId = runnerId, UserId = userId });
        dbContext.SaveChanges();
    }

    private static Runner CreateRunner(Guid runnerId, string name, string purpose, bool isOnline) => new()
    {
        Id = runnerId,
        Name = name,
        Purpose = purpose,
        CapabilitiesJson = RunnerCapabilities.Serialize(RunnerCapabilities.DefaultsForPurpose(purpose), purpose),
        Status = isOnline ? "online" : "offline"
    };

    private static ProjectMember CreateProjectMember(Guid projectId, Guid userId) => new()
    {
        ProjectId = projectId,
        UserId = userId,
        Role = ProjectRole.ProjectAdmin,
        JoinedAt = DateTime.UtcNow,
        IsActive = true
    };
}
