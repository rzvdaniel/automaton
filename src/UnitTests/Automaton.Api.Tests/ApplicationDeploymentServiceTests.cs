using System.IO.Compression;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Automaton.Api.Hubs.AutomatonHub;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ApplicationDeploymentServiceTests
{
    [Test]
    public async Task IncompatibleRunnerFailsBeforeManagedArtifactDeployment()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            runnerApiCompatibilityVersion: RunnerApiCompatibilityVersions.Legacy);
        await DeploymentPackageTestScenarios.AssertIncompatibleRunnerIsRejectedAsync(
            fixture,
            "Upgrade the Runner before deploying",
            expectedGenericRuntimeDeployed: false,
            expectedStatus: DeploymentTargetStatus.Failed);
    }

    [Test]
    public async Task ManagedArtifactRunnerV2FailsBeforeContainerApplicationUpload()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            runnerApiCompatibilityVersion: RunnerApiCompatibilityVersions.ManagedApplicationArtifacts);

        var package = fixture.CreatePackage(hasRuntime: true);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                package,
                clean: true,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("container-based application execution"));
        Assert.That(fixture.RunnerInvocationStarted.Task.IsCompleted, Is.False);
    }

    [Test]
    public async Task IncompatibleRunnerDoesNotClearAnExistingGenericRuntimeDeployment()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            initialGenericMarker: true,
            runnerApiCompatibilityVersion: RunnerApiCompatibilityVersions.Legacy);

        var package = fixture.CreatePackage(hasRuntime: true);
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                package,
                clean: true,
                CancellationToken.None));

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(item => item.Id == fixture.Target.Id);
        Assert.That(target.GenericRuntimeDeployed, Is.True);
    }

    [Test]
    public async Task AcceptingPackage_PersistsOperationBeforeReturning()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var acceptance = await DeploymentAcceptanceTestScenarios.AcceptPackageAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(acceptance.Status, Is.EqualTo(DeploymentOperationStatuses.Pending));
            Assert.That(acceptance.PersistedOperationId, Is.EqualTo(acceptance.ResponseOperationId));
            Assert.That(acceptance.PackageExists, Is.True);
            Assert.That(acceptance.PublishedOperationId, Is.EqualTo(acceptance.ResponseOperationId));
        });
    }

    [Test]
    public async Task AcceptingPackage_RequiresAppUpdatePermission()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        await DeploymentAcceptanceTestScenarios.AssertViewerCannotDeployAsync(fixture);
    }

    [Test]
    public async Task InterruptedRunningOperation_IsReclaimedByANewApiInstance()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var result = await DeploymentOperationTestScenarios.ReclaimExpiredOperationAsync(fixture);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(DeploymentOperationStatuses.Failed));
            Assert.That(result.Reclaimed, Is.True);
            Assert.That(result.ErrorMessage, Does.Contain("missing-deployment.zip"));
        });
    }

    [Test]
    public async Task LiveExecutionLease_PreventsDuplicateExecutionFromStealingOperation()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var attemptId = Guid.NewGuid();
        var operation = await DeploymentOperationTestData.CreateAsync(
            fixture.Db,
            fixture.AppId,
            fixture.OwnerId,
            DeploymentOperationStatuses.Running,
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-deployment.zip"),
            executorInstanceId: "another-live-api-instance",
            executionAttemptId: attemptId,
            leaseExpiresAtUtc: DateTime.UtcNow.AddMinutes(5));

        var operations = DeploymentOperationTestScenarios.CreateService(fixture);

        Assert.ThrowsAsync<DeploymentOperationLeaseActiveException>(
            async () => await operations.ExecuteAsync(operation.Id, CancellationToken.None));
        var unchanged = await fixture.Db.DeploymentOperations.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(unchanged.Status, Is.EqualTo(DeploymentOperationStatuses.Running));
            Assert.That(unchanged.ExecutorInstanceId, Is.EqualTo("another-live-api-instance"));
            Assert.That(unchanged.ExecutionAttemptId, Is.EqualTo(attemptId));
        });
    }

    [Test]
    public async Task TerminalOperation_IsNotReexecuted()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var operation = await DeploymentOperationTestData.CreateAsync(
            fixture.Db,
            fixture.AppId,
            fixture.OwnerId,
            DeploymentOperationStatuses.Succeeded,
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-deployment.zip"),
            completedAtUtc: DateTime.UtcNow);

        var operations = DeploymentOperationTestScenarios.CreateService(fixture);

        await operations.ExecuteAsync(operation.Id, CancellationToken.None);

        var unchanged = await fixture.Db.DeploymentOperations.SingleAsync();
        Assert.That(unchanged.Status, Is.EqualTo(DeploymentOperationStatuses.Succeeded));
    }

    internal sealed class DeploymentFixture : IAsyncDisposable
    {
        private readonly string rootPath;
        private readonly string packagePath;
        private readonly bool routeRootAsFile;

        public ApplicationDbContext Db { get; }
        public ApplicationDeploymentService Service { get; }
        public ApiInstanceIdentityService ApiIdentity { get; }
        public Guid OwnerId { get; }
        public Guid AppId { get; }
        public AppDeploymentTarget Target { get; }
        public DeployAppPackageResult RunnerResult { get; }
        public TaskCompletionSource<bool> RunnerInvocationStarted { get; }
        public string PackagePath => packagePath;
        public string RootPath => rootPath;
        public string RouteFilePath => Path.Combine(rootPath, "routes", "managed-runtime-routes.yml");

        internal DeploymentFixture(
            ApplicationDbContext db,
            ApplicationDeploymentService service,
            Guid ownerId,
            Guid appId,
            AppDeploymentTarget target,
            DeployAppPackageResult runnerResult,
            string rootPath,
            string packagePath,
            bool routeRootAsFile,
            TaskCompletionSource<bool> runnerInvocationStarted,
            ApiInstanceIdentityService apiIdentity)
        {
            Db = db;
            ApiIdentity = apiIdentity;
            Service = service;
            OwnerId = ownerId;
            AppId = appId;
            Target = target;
            RunnerResult = runnerResult;
            this.rootPath = rootPath;
            this.packagePath = packagePath;
            this.routeRootAsFile = routeRootAsFile;
            RunnerInvocationStarted = runnerInvocationStarted;
        }

        public static Task<DeploymentFixture> CreateAsync(
            bool initialGenericMarker = false,
            bool routeRootAsFile = false,
            bool routeStateConflict = false,
            bool routeThenStateConflict = false,
            DeployAppPackageResult? runnerResult = null,
            TimeSpan? runnerInvocationDelay = null,
            string? runnerApiCompatibilityVersion = null)
            => DeploymentFixtureFactory.CreateAsync(new DeploymentFixtureSettings(
                initialGenericMarker,
                routeRootAsFile,
                routeStateConflict,
                routeThenStateConflict,
                runnerResult,
                runnerInvocationDelay,
                runnerApiCompatibilityVersion));
        public IFormFile CreatePackage(bool hasRuntime, IReadOnlyDictionary<string, string>? sourceFiles = null)
            => DeploymentPackageArchiveFactory.Create(AppId, packagePath, hasRuntime, sourceFiles);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(rootPath))
                Directory.Delete(rootPath, recursive: true);
            else if (routeRootAsFile && File.Exists(Path.Combine(rootPath, "routes")))
                File.Delete(Path.Combine(rootPath, "routes"));
        }
    }

}
