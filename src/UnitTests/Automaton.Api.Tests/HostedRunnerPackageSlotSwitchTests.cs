using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerPackageSlotSwitchTests
{
    [Test]
    public async Task ActiveApiSlotCanServePackageStagedBeforeCutover()
    {
        await using var db = HostedRunnerAuditTestData.CreateDbContext();
        var packagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var packageBytes = "package staged by the previous API slot"u8.ToArray();
        await File.WriteAllBytesAsync(packagePath, packageBytes);
        try
        {
            var apiKey = CreateApiKey();
            var runner = CreateRunner(apiKey.Id);
            var app = new WebApp
            {
                Id = Guid.NewGuid(), Name = "slot-switch-test", Summary = string.Empty,
                Description = string.Empty, Definition = string.Empty
            };
            var operationId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            var target = CreateTarget(app.Id, runner, operationId, attemptId);
            db.AddRange(apiKey, runner, app, target, new DeploymentOperation
            {
                Id = operationId,
                WebAppId = app.Id,
                OwnerUserId = Guid.NewGuid(),
                Status = DeploymentOperationStatuses.Running,
                PackagePath = packagePath,
                ExecutionAttemptId = attemptId
            });
            await db.SaveChangesAsync();

            var oldApiSlot = CreateCommandService(db);
            await using (var preCutoverRead = await oldApiSlot.OpenPackageAsync(
                apiKey.Id, operationId, attemptId, CancellationToken.None))
            {
                Assert.That(preCutoverRead, Is.Not.Null);
            }

            var activeApiSlot = CreateCommandService(db);
            await using var package = await activeApiSlot.OpenPackageAsync(
                apiKey.Id, operationId, attemptId, CancellationToken.None);

            Assert.That(package, Is.Not.Null);
            using var received = new MemoryStream();
            await package!.CopyToAsync(received);
            Assert.That(received.ToArray(), Is.EqualTo(packageBytes));
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static HostedRunnerDeploymentCommandService CreateCommandService(ApplicationDbContext db)
    {
        var packageStore = new DeploymentPackageStore(
            new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath() },
            Options.Create(new DeploymentPackageStorageOptions()));
        return new HostedRunnerDeploymentCommandService(
            db,
            packageStore,
            null!,
            new AppDeploymentTargetStateService(db),
            Options.Create(new HostedRunnerOptions()));
    }

    private static ApiKey CreateApiKey() => new()
    {
        Id = Guid.NewGuid(), Name = "runner", HashedKey = "hash", KeyPrefix = "prefix",
        CreatedAt = DateTime.UtcNow
    };

    private static Runner CreateRunner(Guid apiKeyId) => new()
    {
        Id = Guid.NewGuid(), Name = "hosted-runner", Purpose = RunnerPurposes.Managed, ApiKeyId = apiKeyId
    };

    private static AppDeploymentTarget CreateTarget(
        Guid appId,
        Runner runner,
        Guid operationId,
        Guid attemptId)
    {
        var target = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = "Live",
            Slug = "live",
            IsDefault = true,
            ExecutionRunnerId = runner.Id,
            ExecutionRunner = runner,
            ActiveDeploymentOperationId = operationId,
            ActiveDeploymentExecutionAttemptId = attemptId,
            Status = DeploymentTargetStatus.Deploying,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        target.SynchronizeDefaultConstraint();
        return target;
    }
}
