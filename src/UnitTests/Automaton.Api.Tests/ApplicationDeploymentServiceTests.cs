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
    public async Task RunnerConnectedToAnotherApiCanReceiveDurableDeploymentByStableIdentity()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var operationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        fixture.Target.ExecutionRunner!.ApiInstanceId = "another-api";
        await fixture.Db.SaveChangesAsync();
        var package = fixture.CreatePackage(hasRuntime: true);

        var response = await fixture.Service.DeployPackageAsync(fixture.AppId, fixture.OwnerId, fixture.Target.Slug,
            package, clean: true, CancellationToken.None, operationId, attemptId);

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(item => item.Id == fixture.Target.Id);
        Assert.That(response.Success, Is.True);
        Assert.That(fixture.RunnerInvocationStarted.Task.IsCompleted, Is.True);
        Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Running));
        Assert.That(target.GenericRuntimeDeployed, Is.True);
        Assert.That(target.ActiveDeploymentOperationId, Is.Null);
        Assert.That(target.ActiveDeploymentExecutionAttemptId, Is.Null);
    }

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
    public async Task DeployPackageAsync_PersistsOpaqueGenericSourceFiles()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var package = fixture.CreatePackage(
            hasRuntime: true,
            sourceFiles: new Dictionary<string, string>
            {
                [".gitattributes"] = "*.sh text eol=lf\n",
                ["build.sh"] = "#!/bin/sh\n",
                ["Program.cs"] = "class Program {}\n",
                ["Generic.csproj"] = "<Project />\n",
                ["appsettings"] = "opaque config\n"
            });

        var response = await fixture.Service.DeployPackageAsync(
            fixture.AppId,
            fixture.OwnerId,
            fixture.Target.Slug,
            package,
            clean: false,
            CancellationToken.None);

        Assert.That(response.Success, Is.True);
        var sourceRoot = Path.Combine(
            fixture.RootPath,
            "AppData",
            "apps",
            fixture.OwnerId.ToString(),
            fixture.AppId.ToString(),
            "source");
        foreach (var file in new[] { ".gitattributes", "build.sh", "Program.cs", "Generic.csproj", "appsettings" })
            Assert.That(File.Exists(Path.Combine(sourceRoot, file)), Is.True, file);
    }

    [Test]
    public async Task DeployPackageAsync_RejectsSourcePathTraversal()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var package = fixture.CreatePackage(
            hasRuntime: true,
            sourceFiles: new Dictionary<string, string>
            {
                ["../outside.txt"] = "must not escape"
            });

        var exception = Assert.ThrowsAsync<InvalidDataException>(
            async () => await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                package,
                clean: false,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("escapes the app source directory"));
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
    public async Task RedeliveryAfterLeaseExpiry_ReclaimsOperationWithoutManualStateReset()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var result = await DeploymentOperationTestScenarios.ReclaimAfterLeaseExpiryAsync(fixture);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(DeploymentOperationStatuses.Succeeded));
            Assert.That(result.Reclaimed, Is.True);
            Assert.That(result.AttemptChanged, Is.True);
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

    [Test]
    public async Task CallerCancellationDuringRunnerExecution_LeavesOperationDurable()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            runnerInvocationDelay: TimeSpan.FromMilliseconds(250));
        var result = await DeploymentOperationTestScenarios.CancelRunnerExecutionAsync(fixture);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(DeploymentOperationStatuses.Succeeded));
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.HasAttempt, Is.True);
        });
    }

    [Test]
    public async Task DeployPackageAsync_CompletesTargetOnlyAfterApiFinalization()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var result = await DeploymentPackageTestScenarios.DeployRuntimeAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.TargetStatus, Is.EqualTo(DeploymentTargetStatus.Running));
            Assert.That(result.GenericRuntimeDeployed, Is.True);
            Assert.That(result.HasSuccessfulDeploymentTime, Is.True);
            Assert.That(result.ApplicationContractHasServer, Is.True);
            Assert.That(result.RouteExists, Is.True);
            Assert.That(result.RouteContent, Does.Contain("http://runner:8080"));
        });
    }

    [Test]
    public async Task DeployPackageAsync_SuccessfulNoRuntimeReplacementRemovesGenericRoute()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(initialGenericMarker: true);
        var package = fixture.CreatePackage(hasRuntime: false);

        var response = await fixture.Service.DeployPackageAsync(
            fixture.AppId,
            fixture.OwnerId,
            fixture.Target.Slug,
            package,
            clean: false,
            CancellationToken.None);

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(target => target.Id == fixture.Target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Running));
            Assert.That(target.GenericRuntimeDeployed, Is.False);
            Assert.That(File.Exists(fixture.RouteFilePath), Is.False);
        });
    }

    [Test]
    public async Task DeployPackageAsync_RunnerFailureMarksTargetFailedWithoutChangingMarkers()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            initialGenericMarker: true,
            runnerResult: new DeployAppPackageResult
            {
                DeploymentTargetId = Guid.Empty,
                Success = false,
                Message = "candidate failed"
            });
        fixture.RunnerResult.DeploymentTargetId = fixture.Target.Id;
        var package = fixture.CreatePackage(hasRuntime: true);

        var response = await fixture.Service.DeployPackageAsync(
            fixture.AppId,
            fixture.OwnerId,
            fixture.Target.Slug,
            package,
            clean: false,
            CancellationToken.None);

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(target => target.Id == fixture.Target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(target.GenericRuntimeDeployed, Is.True);
        });
    }

    [Test]
    public async Task DeployPackageAsync_FinalizationFailurePreservesOriginalErrorAndDoesNotClaimLegacyRuntime()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            initialGenericMarker: true,
            routeRootAsFile: true);
        var result = await DeploymentPackageTestScenarios.FailFinalizationAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Does.Not.Contain("no longer the active operation"));
            Assert.That(result.TargetStatus, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(result.GenericRuntimeDeployed, Is.True);
        });
    }

    [Test]
    public async Task DeployPackageAsync_SecondaryFailureFencingConflictDoesNotReplaceOriginalError()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(routeStateConflict: true);
        var package = fixture.CreatePackage(hasRuntime: true);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                package,
                clean: false,
                CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("route refresh failed"));
    }

    [Test]
    public async Task DeployPackageAsync_FirstDeploymentCompletionFailureRemovesPendingRoute()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(routeThenStateConflict: true);
        var result = await DeploymentPackageTestScenarios.FailInitialRouteCompletionAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Does.Contain("no longer the active operation"));
            Assert.That(result.GenericRuntimeDeployed, Is.False);
            Assert.That(result.RouteExists, Is.False);
        });
    }

    [Test]
    public async Task DeployPackageAsync_NoRuntimeCompletionFailureRestoresCommittedRoute()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            initialGenericMarker: true,
            routeThenStateConflict: true);
        var result = await DeploymentPackageTestScenarios.FailNoRuntimeRouteCompletionAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Does.Contain("no longer the active operation"));
            Assert.That(result.GenericRuntimeDeployed, Is.True);
            Assert.That(result.RouteExists, Is.True);
            Assert.That(result.RouteContent, Does.Contain("http://runner:8080"));
        });
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
