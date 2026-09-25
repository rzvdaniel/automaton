using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class AppAuthorizationTestData
{
    public static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    public static UserContextService CreateUserContextService(Guid userId) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
        }
    });

    public static void SeedProjectScopedApp(
        ApplicationDbContext dbContext,
        Guid appId,
        Guid projectId,
        Guid projectOwnerId,
        Guid collaboratorId)
    {
        dbContext.Projects.Add(CreateProject(projectId, projectOwnerId, "project"));
        dbContext.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = collaboratorId,
            Role = ProjectRole.Developer,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Project App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            PublicUrl = "project-app",
            IsPublished = true,
            AccessStatus = AppAccessStatuses.Active,
            ProjectId = projectId,
            Scope = AccessScope.Project,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.ProjectApps.Add(new ProjectApp
        {
            ProjectId = projectId,
            WebAppId = appId,
            AddedAt = DateTime.UtcNow,
            AddedBy = projectOwnerId
        });
        dbContext.WebAppUsers.Add(new WebAppUser { WebAppId = appId, UserId = collaboratorId });
        dbContext.SaveChanges();
    }

    public static void SeedApp(ApplicationDbContext dbContext, Guid appId, Guid ownerId, string slug)
    {
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "App",
            Summary = string.Empty,
            Description = string.Empty,
            PublicUrl = slug,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser { WebAppId = appId, UserId = ownerId });
        dbContext.SaveChanges();
    }

    public static Project CreateProject(Guid projectId, Guid ownerId, string name) => new()
    {
        Id = projectId,
        Name = name,
        DisplayName = name,
        Description = string.Empty,
        OwnerId = ownerId,
        Created = DateTime.UtcNow,
        Updated = DateTime.UtcNow,
        IsActive = true
    };

    public static CustomStep CreateStep(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        DisplayName = name,
        Description = string.Empty,
        MoreInfo = string.Empty,
        Icon = "code",
        Category = "App Steps",
        Definition = "{\"language\":\"Python\",\"codeInputVariables\":[],\"codeOutputVariables\":[]}",
        Created = DateTime.UtcNow,
        Updated = DateTime.UtcNow
    };

    public sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "automaton-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
