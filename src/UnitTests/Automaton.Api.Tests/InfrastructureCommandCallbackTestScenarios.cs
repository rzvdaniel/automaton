using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class InfrastructureCommandCallbackTestScenarios
{
    public static async Task<CallbackFixture> CreateAsync(
        string hostedRunnerStatus = HostedRunnerStatuses.Provisioning,
        string deploymentAction = HostedRunnerDeploymentActions.Restart,
        string deploymentStatus = HostedRunnerDeploymentStatuses.Queued,
        int? runtimePort = null)
    {
        var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = CreateInfrastructureRunner(apiKeyId);
        var host = CreateRunnerHost(runner);
        var hosted = CreateHostedRunner(host, hostedRunnerStatus, runtimePort);
        var deployment = CreateDeployment(hosted, deploymentAction, deploymentStatus);
        db.AddRange(runner, host, hosted, deployment);

        await db.SaveChangesAsync();

        return new CallbackFixture(db, apiKeyId, runner, host, hosted, deployment,
            CreateService(db));
    }

    private static Runner CreateInfrastructureRunner(Guid apiKeyId) => new()
    {
        Id = Guid.NewGuid(),
        ApiKeyId = apiKeyId,
        Name = "infra",
        Purpose = RunnerPurposes.Infrastructure
    };

    private static RunnerHost CreateRunnerHost(Runner runner) => new()
    {
        Id = Guid.NewGuid(),
        Name = "host",
        Provider = "test",
        InfrastructureRunnerId = runner.Id,
        InfrastructureRunner = runner
    };

    private static HostedRunner CreateHostedRunner(
        RunnerHost host,
        string status,
        int? runtimePort) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Name = "hosted",
        ContainerName = "hosted",
        RunnerHostId = host.Id,
        Status = status,
        StorageProjectId = 1_000_001,
        RuntimePort = runtimePort
    };

    private static HostedRunnerDeployment CreateDeployment(HostedRunner hosted, string action, string status) => new()
    {
        Id = Guid.NewGuid(),
        HostedRunnerId = hosted.Id,
        HostedRunner = hosted,
        Action = action,
        Status = status
    };

    private static InfrastructureCommandCallbackService CreateService(
        ApplicationDbContext db)
    {
        var handler = RunnerExecutionTestServices.Lifecycle(
            db,
            new NoOpAuditEventService(),
            null!);
        var resultService = new InfrastructureCommandResultService(db, null!, handler);
        return new InfrastructureCommandCallbackService(db, null!, handler, resultService);
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

internal sealed record CallbackFixture(
    ApplicationDbContext Db,
    Guid ApiKeyId,
    Runner Runner,
    RunnerHost Host,
    HostedRunner HostedRunner,
    HostedRunnerDeployment Deployment,
    InfrastructureCommandCallbackService Service) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
