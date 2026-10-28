using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class RunnerExecutionsControllerTests
{
    [Test]
    public async Task WrongIdentityReturnsForbidden()
    {
        await using var scenario = await Scenario.CreateAsync();
        Authenticate(scenario.Controller, Guid.NewGuid());
        Assert.That((await scenario.Controller.Assignment(scenario.OperationId, CancellationToken.None)).Result, Is.TypeOf<ForbidResult>());
        Assert.That((await scenario.Controller.Result(scenario.OperationId, new(), CancellationToken.None)).Result, Is.TypeOf<ForbidResult>());
    }

    [Test]
    public async Task InvalidResultReturnsBadRequestWithoutCompletingOperation()
    {
        await using var scenario = await Scenario.CreateAsync();
        Authenticate(scenario.Controller, scenario.KeyId);
        Assert.That((await scenario.Controller.Result(scenario.OperationId, new() { Outcome = "Running" }, CancellationToken.None)).Result,
            Is.TypeOf<BadRequestObjectResult>());
        Assert.That((await scenario.Db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).CompletedAtUtc, Is.Null);
    }

    private static void Authenticate(ControllerBase controller, Guid keyId)
        => controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("api_key_id", keyId.ToString())], "test"));

    private sealed class Scenario(ApplicationDbContext db, RunnerExecutionsController controller, Guid operationId, Guid keyId) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public RunnerExecutionsController Controller { get; } = controller;
        public Guid OperationId { get; } = operationId;
        public Guid KeyId { get; } = keyId;
        public ValueTask DisposeAsync() => Db.DisposeAsync();
        public static async Task<Scenario> CreateAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var runner = new Runner { Id = Guid.NewGuid(), Name = "test", ApiKeyId = Guid.NewGuid(), ApplicationProfile = "MachineManagement" };
            db.Runners.Add(runner); await db.SaveChangesAsync();
            var operations = RunnerExecutionTestServices.Operations(db);
            var id = await operations.QueueAsync(runner.Id, Guid.NewGuid(), new(Guid.NewGuid(), null), CancellationToken.None);
            var controller = CreateController(operations, runner.Id);
            return new(db, controller, id, runner.ApiKeyId.Value);
        }
        private static RunnerExecutionsController CreateController(RunnerExecutionOperationService operations, Guid runnerId)
        {
            var controller = new RunnerExecutionsController(null!, null!, operations)
            {
                ControllerContext = new() { HttpContext = new DefaultHttpContext() }
            };
            controller.Request.Headers["X-Runner-Id"] = runnerId.ToString();
            return controller;
        }
    }
}
