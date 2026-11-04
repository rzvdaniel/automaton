using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

public sealed class DeletedProjectBehaviorTests
{
    [Test]
    public async Task DeletedProjectsAreNotListed()
    {
        var (db, owner, project) = CreateDeletedProject();
        var user = AppAuthorizationTestData.CreateUserContextService(owner);
        var service = new ProjectService(db, user, new ResourceAuthorizationService(db, user), new NoAudit());

        Assert.That(await service.ListUserProjectsAsync(), Is.Empty);
    }

    [Test]
    public async Task AddingAppToDeletedProjectReturnsBadRequest()
    {
        var (db, owner, project) = CreateDeletedProject();
        var app = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, app, owner, "deleted-project-app");
        var user = AppAuthorizationTestData.CreateUserContextService(owner);
        var audit = new NoAudit();
        var authorization = new ProjectResourceAuthorizationService(db, audit);
        var resources = new ProjectResourceService(db, user, authorization,
            new ProjectCredentialResourceService(db, authorization, audit), audit);
        var controller = new ProjectResourcesController(resources, NullLogger<ProjectResourcesController>.Instance)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner.ToString())], "Test")) } }
        };

        var response = await controller.AddAppToProject(project, app);
        Assert.That(response.Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That((response.Result as BadRequestObjectResult)?.Value?.ToString(), Does.Contain("This project was deleted"));
        Assert.That(await db.ProjectApps.AnyAsync(link => link.WebAppId == app), Is.False);
    }

    [Test]
    public async Task RunnerAssignmentExplainsDeletedProject()
    {
        var (db, owner, project) = CreateDeletedProject();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, owner, "deleted-project-runner");
        var app = await db.WebApps.SingleAsync(item => item.Id == appId);
        app.ProjectId = project;
        app.Scope = AccessScope.Project;
        var runnerId = Guid.NewGuid();
        db.Runners.Add(new Runner { Id = runnerId, Name = "runner", Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement, Status = "Online" });
        db.ProjectRunners.Add(new ProjectRunner { ProjectId = project, RunnerId = runnerId });
        await db.SaveChangesAsync();
        var user = AppAuthorizationTestData.CreateUserContextService(owner);
        var policy = new AppRunnerAuthorizationPolicy(db, new ResourceAuthorizationService(db, user));

        Assert.That(async () => await policy.AuthorizeAsync(app, runnerId, owner),
            Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("project was deleted"));
    }

    private static (ApplicationDbContext Db, Guid Owner, Guid Project) CreateDeletedProject()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var project = Guid.NewGuid();
        var entity = AppAuthorizationTestData.CreateProject(project, owner, "deleted");
        entity.IsActive = false;
        db.Projects.Add(entity);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project, UserId = owner,
            Role = ProjectRole.ProjectAdmin, JoinedAt = DateTime.UtcNow, IsActive = true });
        db.SaveChanges();
        return (db, owner, project);
    }

    private sealed class NoAudit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
