using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppAuthorizationRegressionTests
{
    [Test]
    public async Task GetAccessibleAppInfoByIdAsync_ReturnsNullForUnauthorizedApp()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedApp(dbContext, appId, ownerId, "private-app");

        var service = CreateAppsService(dbContext, callerId);

        var result = await service.GetAccessibleAppInfoByIdAsync(appId, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAccessibleAppInfoByIdAsync_ReturnsAppForOwner()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedApp(dbContext, appId, ownerId, "owner-app");

        var service = CreateAppsService(dbContext, ownerId);

        var result = await service.GetAccessibleAppInfoByIdAsync(appId, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(appId));
        Assert.That(result.OwnerId, Is.EqualTo(ownerId));
    }

    [Test]
    public async Task GetPublishedAppInfoAsync_UsesProjectOwnerAsOwnerId()
    {
        var projectOwnerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        dbContext.Projects.Add(new Project
        {
            Id = projectId,
            Name = "project",
            DisplayName = "Project",
            Description = string.Empty,
            OwnerId = projectOwnerId,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
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
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = collaboratorId
        });
        await dbContext.SaveChangesAsync();

        var service = CreateAppsService(dbContext, collaboratorId);

        var result = await service.GetPublishedAppInfoAsync("project-app", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.OwnerId, Is.EqualTo(projectOwnerId));
    }

    [Test]
    public void Get_ReturnsProjectOwnerAsUserId_ForProjectScopedApp()
    {
        var projectOwnerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        using var tempRoot = new TemporaryDirectory();
        using var dbContext = CreateDbContext();
        SeedProjectScopedApp(dbContext, appId, projectId, projectOwnerId, collaboratorId);

        var service = CreateAppsService(dbContext, collaboratorId, tempRoot.Path);

        var result = service.Get(appId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UserId, Is.EqualTo(projectOwnerId));
    }

    [Test]
    public void List_ReturnsProjectOwnerAsUserId_ForProjectScopedApp()
    {
        var projectOwnerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        using var tempRoot = new TemporaryDirectory();
        using var dbContext = CreateDbContext();
        SeedProjectScopedApp(dbContext, appId, projectId, projectOwnerId, collaboratorId);

        var service = CreateAppsService(dbContext, collaboratorId, tempRoot.Path);

        var result = service.List(projectId).Single();

        Assert.That(result.UserId, Is.EqualTo(projectOwnerId));
    }

    [Test]
    public async Task GetLinkedStepsAsync_ReturnsOnlyStepsLinkedToApp()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var linkedStepId = Guid.NewGuid();
        var unrelatedStepId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();

        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Test App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = ownerId
        });

        dbContext.CustomSteps.AddRange(
            CreateStep(linkedStepId, "linked-step"),
            CreateStep(unrelatedStepId, "unrelated-step"));

        dbContext.CustomStepUsers.AddRange(
            new CustomStepUser { CustomStepId = linkedStepId, UserId = ownerId },
            new CustomStepUser { CustomStepId = unrelatedStepId, UserId = ownerId });

        dbContext.WebAppSteps.Add(new WebAppStep
        {
            WebAppId = appId,
            CustomStepId = linkedStepId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = ownerId
        });

        await dbContext.SaveChangesAsync();

        var service = new AppsStepExecutionService(dbContext, null!, null!);

        var steps = (await service.GetLinkedStepsAsync(appId)).ToList();

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].Id, Is.EqualTo(linkedStepId));
        Assert.That(steps[0].Name, Is.EqualTo("linked-step"));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AppsService CreateAppsService(ApplicationDbContext dbContext, Guid userId, string? contentRootPath = null)
    {
        var userContextService = new UserContextService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "Test"))
            }
        });

        var authorizationService = new ResourceAuthorizationService(dbContext, userContextService);
        var environment = new StubWebHostEnvironment(contentRootPath ?? Path.GetTempPath());
        var settingsService = new SettingsService(
            dbContext,
            userContextService,
            Options.Create(new AppFileRuleBaselineOptions
            {
                RemixSourceExclude = [".env", ".env.*", "secrets.json", "secrets", "secrets/**"],
                RemixFrontendExclude = []
            }));
        var fileStorageService = new AppFileStorageService(environment, settingsService);

        return new AppsService(
            dbContext,
            userContextService,
            null!,
            authorizationService,
            fileStorageService,
            null!,
            environment,
            new NoOpAuditEventService());
    }

    private static void SeedProjectScopedApp(
        ApplicationDbContext dbContext,
        Guid appId,
        Guid projectId,
        Guid projectOwnerId,
        Guid collaboratorId)
    {
        dbContext.Projects.Add(new Project
        {
            Id = projectId,
            Name = "project",
            DisplayName = "Project",
            Description = string.Empty,
            OwnerId = projectOwnerId,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            IsActive = true
        });
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
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = collaboratorId
        });
        dbContext.SaveChanges();
    }

    private static void SeedApp(ApplicationDbContext dbContext, Guid appId, Guid ownerId, string slug)
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
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = ownerId
        });
        dbContext.SaveChanges();
    }

    private static Api.Entities.CustomStep CreateStep(Guid id, string name)
    {
        return new Api.Entities.CustomStep
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
    }

    private sealed class StubWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class TemporaryDirectory : IDisposable
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

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(
            AuditEventWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
