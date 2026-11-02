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
}
