using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class AppActionDurableOperationTests
{
    [Test]
    public async Task WrongExecutionIdentityIsRejectedBeforeEitherRecordChanges()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.QueueAsync();
        var result = fixture.Result(Guid.NewGuid());
        Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ApplyAsync(fixture.Key, fixture.Id, result, CancellationToken.None));
        Assert.That((await fixture.Db.RunnerExecutionOperations.SingleAsync()).CompletedAtUtc, Is.Null);
        Assert.That((await fixture.Db.AppModuleExecutions.SingleAsync()).CompletedAtUtc, Is.Null);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid Id { get; } = Guid.NewGuid(); public Guid Key { get; } = Guid.NewGuid();
        public Guid RunnerId { get; } = Guid.NewGuid();
        public RunnerExecutionOperationService Service { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.Db.Runners.Add(new() { Id = fixture.RunnerId, ApiKeyId = fixture.Key, Name = "test", RunnerUsers = [] });
            await fixture.Db.SaveChangesAsync(); fixture.Service = RunnerExecutionTestServices.Operations(fixture.Db); return fixture;
        }
        public async Task QueueAsync()
        {
            var app = Guid.NewGuid(); var target = Guid.NewGuid();
            Db.AppModuleExecutions.Add(new() { Id = Id, AppId = app, DeploymentTargetId = target, RunnerId = RunnerId,
                Status = "Dispatching", ModuleKey = "acceptance", ModuleType = "Action", TriggerType = "Manual", ExecutionDeadlineUtc = DateTime.UtcNow.AddMinutes(1) });
            await Service.QueueAsync(RunnerId, Guid.NewGuid(), new(null, null, AppActionCommand.Create("execute",
                new ExecuteAppActionModuleRequest { ExecutionId = Id, AppId = app, DeploymentTargetId = target, ActionKey = "acceptance",
                    Input = JsonSerializer.SerializeToElement(new { value = "private-value" }) })), CancellationToken.None, Id);
        }
        public RunnerExecutionResult Result(Guid? id = null) => new() { Outcome = "Succeeded", ActionResult = JsonSerializer.SerializeToElement(
            new AppActionExecutionResult { ExecutionId = id ?? Id, Status = "Succeeded", ExitCode = 0,
                StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow, LogsExpireAtUtc = DateTime.UtcNow.AddHours(24) }) };
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
