using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerInvalidCommandResultTests
{
    [Test]
    public async Task RejectAsync_FailsCurrentOperationAndReleasesDeploymentTarget()
    {
        await using var scenario = await InvalidCommandResultScenario.CreateAsync();
        var outcome = await scenario.RejectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo("accepted"));
            Assert.That(scenario.Operation.Status, Is.EqualTo(DeploymentOperationStatuses.Failed));
            Assert.That(scenario.Operation.ErrorMessage, Is.EqualTo("The command payload is invalid."));
            Assert.That(scenario.Operation.CompletedAtUtc, Is.Not.Null);
            Assert.That(scenario.Operation.LeaseExpiresAtUtc, Is.Null);
            Assert.That(scenario.Target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(scenario.Target.ActiveDeploymentOperationId, Is.Null);
            Assert.That(scenario.Target.ActiveDeploymentExecutionAttemptId, Is.Null);
        });
    }
}

internal sealed class InvalidCommandResultScenario : IAsyncDisposable
{
    private readonly ApplicationDbContext db;
    private readonly HostedRunnerDeploymentResultService results;
    private readonly Guid apiKeyId;
    private readonly Guid attemptId;

    private InvalidCommandResultScenario(
        ApplicationDbContext db,
        HostedRunnerDeploymentResultService results,
        Guid apiKeyId,
        Guid operationId,
        Guid attemptId,
        DeploymentOperation operation,
        AppDeploymentTarget target)
    {
        this.db = db;
        this.results = results;
        this.apiKeyId = apiKeyId;
        OperationId = operationId;
        this.attemptId = attemptId;
        Operation = operation;
        Target = target;
    }

    public Guid OperationId { get; }
    public DeploymentOperation Operation { get; }
    public AppDeploymentTarget Target { get; }

    public static async Task<InvalidCommandResultScenario> CreateAsync()
    {
        var db = HostedRunnerAuditTestData.CreateDbContext();
        var data = CreateDeploymentData();
        db.AddRange(data.ApiKey, data.Runner, data.App, data.Target, data.Operation);
        await db.SaveChangesAsync();
        var results = CreateResultService(db);
        return new InvalidCommandResultScenario(db, results,
            data.ApiKey.Id, data.Operation.Id, data.Operation.ExecutionAttemptId!.Value,
            data.Operation, data.Target);
    }

    public async Task<string> RejectAsync()
    {
        var outcome = await results.RejectAsync(apiKeyId, OperationId,
            new HostedRunnerDeploymentCommandRejectionRequest(attemptId, "The command payload is invalid."),
            CancellationToken.None);
        await db.Entry(Operation).ReloadAsync();
        await db.Entry(Target).ReloadAsync();
        return outcome;
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
    }

    private static InvalidCommandDeploymentData CreateDeploymentData()
    {
        var operationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(), Name = "runner", HashedKey = "hash", KeyPrefix = "prefix", CreatedAt = DateTime.UtcNow
        };
        var runner = new Runner
        {
            Id = runnerId, Name = "hosted", Purpose = RunnerPurposes.Managed, ApiKeyId = apiKey.Id
        };
        var app = new WebApp { Id = appId, Name = "app", Summary = "", Description = "", Definition = "" };
        var target = CreateTarget(appId, operationId, attemptId, runnerId, app, runner);
        var operation = new DeploymentOperation
        {
            Id = operationId, WebAppId = appId, OwnerUserId = Guid.NewGuid(), Status = DeploymentOperationStatuses.Running,
            PackagePath = "unused.zip", ExecutionAttemptId = attemptId, LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(4)
        };
        return new InvalidCommandDeploymentData(apiKey, runner, app, target, operation);
    }

    private static AppDeploymentTarget CreateTarget(
        Guid appId,
        Guid operationId,
        Guid attemptId,
        Guid runnerId,
        WebApp app,
        Runner runner)
    {
        var target = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(), WebAppId = appId, WebApp = app, Name = "Live", Slug = "live", IsDefault = true,
            ExecutionRunnerId = runnerId, ExecutionRunner = runner, Status = DeploymentTargetStatus.Deploying,
            ActiveDeploymentOperationId = operationId, ActiveDeploymentExecutionAttemptId = attemptId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        target.SynchronizeDefaultConstraint();
        return target;
    }

    private static HostedRunnerDeploymentResultService CreateResultService(ApplicationDbContext db)
    {
        var targetState = new AppDeploymentTargetStateService(db);
        var commands = new HostedRunnerDeploymentCommandService(
            db, null!, null!, targetState, Options.Create(new HostedRunnerOptions()));
        return new HostedRunnerDeploymentResultService(db, commands, null!, null!, targetState);
    }

    private sealed record InvalidCommandDeploymentData(
        ApiKey ApiKey,
        Runner Runner,
        WebApp App,
        AppDeploymentTarget Target,
        DeploymentOperation Operation);
}
