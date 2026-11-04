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

public sealed class ProjectRunnerApiKeyTests
{
    [Test]
    public async Task OwnerCanAddListAndRemoveOwnRunner()
    {
        var (db, project, owner, runner) = Setup();
        var controller = Controller(db, owner);

        Assert.That((await controller.AddRunnerToProject(project, runner)).Result, Is.TypeOf<OkObjectResult>());
        Assert.That((await controller.ListProjectRunners(project)).Result, Is.TypeOf<OkObjectResult>());
        Assert.That(await controller.RemoveRunnerFromProject(project, runner), Is.TypeOf<NoContentResult>());
        Assert.That(await db.ProjectRunners.AnyAsync(link => link.ProjectId == project && link.RunnerId == runner), Is.False);
    }

    [Test]
    public async Task NonmemberCannotAddRunnerAndOtherUsersRunnerIsHidden()
    {
        var (db, project, owner, runner) = Setup();
        var stranger = Guid.NewGuid();
        Assert.That((await Controller(db, stranger).AddRunnerToProject(project, runner)).Result, Is.TypeOf<ForbidResult>());
        db.RunnerUsers.Remove(db.RunnerUsers.Single(link => link.RunnerId == runner));
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner, UserId = stranger });
        await db.SaveChangesAsync();

        Assert.That((await Controller(db, owner).AddRunnerToProject(project, runner)).Result, Is.TypeOf<NotFoundObjectResult>());
        Assert.That(await db.ProjectRunners.AnyAsync(), Is.False);
    }

    private static (ApplicationDbContext Db, Guid Project, Guid Owner, Guid Runner) Setup()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var project = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var runner = Guid.NewGuid();
        db.Projects.Add(AppAuthorizationTestData.CreateProject(project, owner, "runner-project"));
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project, UserId = owner,
            Role = ProjectRole.ProjectAdmin, JoinedAt = DateTime.UtcNow, IsActive = true });
        db.Runners.Add(new Runner { Id = runner, Name = "runner", Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement, Status = "Online" });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner, UserId = owner });
        db.SaveChanges();
        return (db, project, owner, runner);
    }

    private static ProjectResourcesController Controller(ApplicationDbContext db, Guid userId)
    {
        var user = AppAuthorizationTestData.CreateUserContextService(userId);
        var audit = new NoAudit();
        var authorization = new ProjectResourceAuthorizationService(db, audit);
        var service = new ProjectResourceService(db, user, authorization,
            new ProjectCredentialResourceService(db, authorization, audit), audit);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        return new ProjectResourcesController(service, NullLogger<ProjectResourcesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
        };
    }

    private sealed class NoAudit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
