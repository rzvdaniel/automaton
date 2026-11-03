using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>A refused delete of a flow or custom step answers 403 or 400, not a generic 500.</summary>
[TestFixture]
public sealed class FlowStepDeleteStatusTests
{
    private static (ApplicationDbContext Db, Guid Owner, Guid Stranger, Guid Project, Guid Resource) Seed()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var project = Guid.NewGuid();
        db.Projects.Add(AppAuthorizationTestData.CreateProject(project, owner, "project"));
        db.SaveChanges();
        return (db, owner, Guid.NewGuid(), project, Guid.NewGuid());
    }

    private static ResourceAuthorizationService Authorization(ApplicationDbContext db, Guid userId)
        => new(db, AppAuthorizationTestData.CreateUserContextService(userId));

    [Test]
    public async Task Flow_ANonOwnerGets403_TheOwnerOfAProjectFlowGets400WithTheReason()
    {
        var (db, owner, stranger, project, flow) = Seed();
        db.FlowUsers.Add(new FlowUser { FlowId = flow, UserId = owner });
        db.ProjectFlows.Add(new ProjectFlow { ProjectId = project, FlowId = flow });
        db.SaveChanges();
        FlowsController Controller(Guid user) => new(new FlowsService(db, null!, null!,
            AppAuthorizationTestData.CreateUserContextService(user), Authorization(db, user), null!));

        var refused = await Controller(stranger).Delete(flow);
        var blocked = await Controller(owner).Delete(flow);
        var unknown = await Controller(stranger).Delete(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That((refused as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(Message(refused), Does.Not.Contain("project"));
            Assert.That((unknown as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden), "an unknown id looks the same");
            Assert.That((blocked as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(Message(blocked), Does.Contain("part of one or more projects"));
        });
    }

    [Test]
    public async Task CustomStep_ANonOwnerGets403_TheOwnerOfAProjectStepGets400WithTheReason()
    {
        var (db, owner, stranger, project, step) = Seed();
        db.CustomStepUsers.Add(new CustomStepUser { CustomStepId = step, UserId = owner });
        db.ProjectCustomSteps.Add(new ProjectCustomStep { ProjectId = project, CustomStepId = step });
        db.SaveChanges();
        CustomStepsController Controller(Guid user) => new(new CustomStepsService(db,
            AppAuthorizationTestData.CreateUserContextService(user), null!, null!, Authorization(db, user), null!));

        var refused = await Controller(stranger).Delete(step);
        var blocked = await Controller(owner).Delete(step);

        Assert.Multiple(() =>
        {
            Assert.That((refused as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(Message(refused), Does.Not.Contain("project"));
            Assert.That((blocked as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(Message(blocked), Does.Contain("part of one or more projects"));
        });
    }

    private static string Message(IActionResult result) => (result as ObjectResult)?.Value?.ToString() ?? string.Empty;
}
