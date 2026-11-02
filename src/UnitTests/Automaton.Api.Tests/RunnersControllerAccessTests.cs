using Automaton.Api.Controllers;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Someone who may not change a Runner gets a refusal, not a server error.</summary>
[TestFixture]
public sealed class RunnersControllerAccessTests
{
    [Test]
    public async Task AStrangerUpdatingSomeoneElsesRunnerIsRefused()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var stranger = Guid.NewGuid();
        var users = AppAuthorizationTestData.CreateUserContextService(stranger);
        var service = new RunnerService(db, users, new ResourceAuthorizationService(db, users),
            runnerDeletionService: null!, connectionSessionService: null!, runnerQueryService: null!,
            flowExecutionService: null!, connectionLifecycleService: null!);

        var result = await new RunnersController(service)
            .Put(Guid.NewGuid(), new Automaton.Api.Models.UpdateRunnerRequest { Name = "renamed" }, default);

        Assert.That(result, Is.TypeOf<ForbidResult>());
    }

    [TestCase("owner", true)]
    [TestCase("ProjectAdmin", false)]
    [TestCase("Developer", false)]
    [TestCase("stranger", false)]
    public async Task OnlyTheRunnersOwnerMayChangeIt(string who, bool allowed)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid runner = Guid.NewGuid(), project = Guid.NewGuid(), owner = Guid.NewGuid(), user = who == "owner" ? owner : Guid.NewGuid();
        db.RunnerUsers.Add(new Automaton.Api.Entities.RunnerUser { RunnerId = runner, UserId = owner });
        db.ProjectRunners.Add(new Automaton.Api.Entities.ProjectRunner { ProjectId = project, RunnerId = runner });
        if (Enum.TryParse<Automaton.Api.Enums.ProjectRole>(who, out var role))
            db.ProjectMembers.Add(new Automaton.Api.Entities.ProjectMember
            {
                ProjectId = project, UserId = user, Role = role, JoinedAt = DateTime.UtcNow, IsActive = true
            });
        await db.SaveChangesAsync();

        var users = AppAuthorizationTestData.CreateUserContextService(user);
        Assert.That(await new ResourceAuthorizationService(db, users).CanUpdateRunnerAsync(runner, user), Is.EqualTo(allowed));
    }
}
