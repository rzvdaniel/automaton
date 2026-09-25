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
    public async Task OwnershipLoss_DoesNotFinalizeDeploymentTarget()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var operationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        fixture.Target.ExecutionRunner!.ApiInstanceId = "another-api";
        await fixture.Db.SaveChangesAsync();
        var package = fixture.CreatePackage(hasRuntime: true);

        Assert.ThrowsAsync<DeploymentOperationRunnerOwnershipChangedException>(async () =>
            await fixture.Service.DeployPackageAsync(fixture.AppId, fixture.OwnerId, fixture.Target.Slug,
                package, clean: true, CancellationToken.None, operationId, attemptId));

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(item => item.Id == fixture.Target.Id);
        Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Deploying));
        Assert.That(target.ActiveDeploymentOperationId, Is.EqualTo(operationId));
        Assert.That(target.ActiveDeploymentExecutionAttemptId, Is.EqualTo(attemptId));
    }

    [Test]
    public async Task IncompatibleRunnerFailsBeforeManagedArtifactDeployment()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            runnerApiCompatibilityVersion: RunnerApiCompatibilityVersions.Legacy);
        await fixture.AssertIncompatibleRunnerIsRejectedAsync(
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
        var acceptance = await fixture.AcceptPackageAsync();

        Assert.Multiple(() =>
        {
            Assert.That(acceptance.Status, Is.EqualTo(DeploymentOperationStatuses.Pending));
            Assert.That(acceptance.PersistedOperationId, Is.EqualTo(acceptance.ResponseOperationId));
            Assert.That(acceptance.PackageExists, Is.True);
            Assert.That(acceptance.PublishedOperationId, Is.EqualTo(acceptance.ResponseOperationId));
        });
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

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
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
        var result = await fixture.ReclaimExpiredOperationAsync();
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

        var operations = fixture.CreateOperationService();

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
        var result = await fixture.ReclaimAfterLeaseExpiryAsync();
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

        var operations = fixture.CreateOperationService();

        await operations.ExecuteAsync(operation.Id, CancellationToken.None);

        var unchanged = await fixture.Db.DeploymentOperations.SingleAsync();
        Assert.That(unchanged.Status, Is.EqualTo(DeploymentOperationStatuses.Succeeded));
    }

    [Test]
    public async Task CallerCancellationDuringRunnerExecution_LeavesOperationDurable()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            runnerInvocationDelay: TimeSpan.FromMilliseconds(250));
        var result = await fixture.CancelRunnerExecutionAsync();
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
        var package = fixture.CreatePackage(hasRuntime: true);

        var response = await fixture.Service.DeployPackageAsync(
            fixture.AppId,
            fixture.OwnerId,
            fixture.Target.Slug,
            package,
            clean: false,
            CancellationToken.None);

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(target => target.Id == fixture.Target.Id);
        var app = await fixture.Db.WebApps.SingleAsync(app => app.Id == fixture.AppId);

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Running));
            Assert.That(target.GenericRuntimeDeployed, Is.True);
            Assert.That(target.LastSuccessfulDeploymentAt, Is.Not.Null);
            Assert.That(app.ApplicationContractJson, Does.Contain("server"));
            Assert.That(File.Exists(fixture.RouteFilePath), Is.True);
            Assert.That(File.ReadAllText(fixture.RouteFilePath), Does.Contain("http://runner:8080"));
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
        var package = fixture.CreatePackage(hasRuntime: true);

        var exception = Assert.ThrowsAsync<IOException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                package,
                clean: false,
                CancellationToken.None));

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(target => target.Id == fixture.Target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Not.Contain("no longer the active operation"));
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(target.GenericRuntimeDeployed, Is.True);
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
        var package = fixture.CreatePackage(hasRuntime: true);

        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                package,
                clean: false,
                CancellationToken.None));

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(target => target.Id == fixture.Target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("no longer the active operation"));
            Assert.That(target.GenericRuntimeDeployed, Is.False);
            Assert.That(File.Exists(fixture.RouteFilePath), Is.False);
        });
    }

    [Test]
    public async Task DeployPackageAsync_NoRuntimeCompletionFailureRestoresCommittedRoute()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            initialGenericMarker: true,
            routeThenStateConflict: true);
        var package = fixture.CreatePackage(hasRuntime: false);

        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                package,
                clean: false,
                CancellationToken.None));

        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(target => target.Id == fixture.Target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("no longer the active operation"));
            Assert.That(target.GenericRuntimeDeployed, Is.True);
            Assert.That(File.Exists(fixture.RouteFilePath), Is.True);
            Assert.That(File.ReadAllText(fixture.RouteFilePath), Does.Contain("http://runner:8080"));
        });
    }

    private sealed class DeploymentFixture : IAsyncDisposable
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

        private DeploymentFixture(
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

        public static async Task<DeploymentFixture> CreateAsync(
            bool initialGenericMarker = false,
            bool routeRootAsFile = false,
            bool routeStateConflict = false,
            bool routeThenStateConflict = false,
            DeployAppPackageResult? runnerResult = null,
            TimeSpan? runnerInvocationDelay = null,
            string? runnerApiCompatibilityVersion = null)
        {
            var rootPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "package-deploy", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var packagePath = Path.Combine(rootPath, "package.zip");
            var routeRoot = Path.Combine(rootPath, "routes");
            if (routeRootAsFile)
                await File.WriteAllTextAsync(routeRoot, "route-root-placeholder");

            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase($"package-deploy-{Guid.NewGuid():N}")
                    .Options);

            var ownerId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var apiIdentity = new ApiInstanceIdentityService();
            var runner = new Runner
            {
                Id = Guid.NewGuid(),
                Name = "Package deployment runner",
                Purpose = RunnerPurposes.Managed,
                ConnectionId = "package-deployment-connection",
                ApiInstanceId = apiIdentity.ApiInstanceId,
                LastHeartbeatAtUtc = DateTime.UtcNow,
                Status = RunnerStatus.Online.ToString(),
                ApiCompatibilityVersion = runnerApiCompatibilityVersion ?? RunnerApiCompatibilityVersions.Current,
                CapabilitiesJson = RunnerCapabilities.Serialize(
                    RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                    RunnerPurposes.Managed)
            };
            var target = AppDeploymentTarget.CreateDefault(appId, runner.Id);
            target.GenericRuntimeDeployed = initialGenericMarker;
            target.ExecutionRunner = runner;
            var app = new WebApp
            {
                Id = appId,
                Name = "Package deployment app",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow,
                ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract()),
                DeploymentTargets = { target }
            };
            db.WebApps.Add(app);
            db.Runners.Add(runner);
            db.WebAppUsers.Add(new WebAppUser { WebAppId = appId, UserId = ownerId });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = Guid.NewGuid(),
                UserId = ownerId,
                Name = "Package deployment hosted runner",
                ContainerName = "package-deployment-runner",
                Status = HostedRunnerStatuses.Running,
                RunnerId = runner.Id,
                RuntimeEndpoint = "http://runner:8080"
            });
            await db.SaveChangesAsync();

            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext()
            };
            var userContext = new UserContextService(httpContextAccessor);
            var settings = new SettingsService(
                db,
                userContext,
                Options.Create(new AppFileRuleBaselineOptions()));
            var environment = new TestWebHostEnvironment { ContentRootPath = rootPath };
            var storage = new AppFileStorageService(environment, settings);
            var uploadRegistry = new FileUploadRegistry();
            var runnerInvocationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var hub = new PackageHubContext(
                uploadRegistry,
                packagePath,
                runnerResult ?? new DeployAppPackageResult
            {
                DeploymentTargetId = target.Id,
                Success = true,
                Message = "package deployed"
            },
                runnerInvocationStarted,
                runnerInvocationDelay);
            var resolver = new FixedTargetResolver(target);
            var routeOptions = Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = routeRoot });
            var routeService = routeThenStateConflict
                ? new RouteThenStateConflictConfigService(
                    db,
                    routeOptions,
                    environment,
                    target.Id)
                : routeStateConflict
                ? new ConflictRouteConfigService(
                    db,
                    routeOptions,
                    environment,
                    target.Id)
                : new BackendRouteConfigService(
                    db,
                    routeOptions,
                    environment,
                    NullLogger<BackendRouteConfigService>.Instance);
            var runnerPolicy = new AppRunnerAssignmentPolicy(db, null!, null!, resolver);
            var service = new ApplicationDeploymentService(
                new DeploymentPackageRunnerService(db, hub, uploadRegistry, apiIdentity),
                null!,
                runnerPolicy,
                resolver,
                new AppDeploymentTargetStateService(db),
                new DeploymentPackageFinalizationService(
                    db,
                    storage,
                    new ConnectedFlowArtifactService(storage, null!, hub),
                    routeService));

            return new DeploymentFixture(
                db,
                service,
                ownerId,
                appId,
                target,
                hub.RunnerResult,
                rootPath,
                packagePath,
                routeRootAsFile,
                runnerInvocationStarted,
                apiIdentity);
        }

        public DeploymentOperationService CreateOperationService(
            ApplicationDeploymentService? deploymentService = null,
            ILogger<DeploymentOperationService>? logger = null)
            => new(
                Db,
                null!,
                null!,
                null!,
                new DeploymentPackageStore(new TestWebHostEnvironment { ContentRootPath = rootPath }),
                deploymentService ?? Service,
                ApiIdentity,
                null!,
                logger ?? NullLogger<DeploymentOperationService>.Instance);

        public async Task<OperationExecutionSnapshot> ReclaimExpiredOperationAsync()
        {
            var operation = await DeploymentOperationTestData.CreateAsync(
                Db,
                AppId,
                OwnerId,
                DeploymentOperationStatuses.Running,
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-deployment.zip"),
                executorInstanceId: "previous-api-instance",
                leaseExpiresAtUtc: DateTime.UtcNow.AddSeconds(-1),
                startedAtUtc: DateTime.UtcNow.AddMinutes(-1));

            await CreateOperationService().ExecuteAsync(operation.Id, CancellationToken.None);
            var result = await Db.DeploymentOperations.SingleAsync();
            return new OperationExecutionSnapshot(
                result.Status,
                result.ExecutorInstanceId != "previous-api-instance",
                false,
                result.ErrorMessage);
        }

        public async Task<OperationExecutionSnapshot> ReclaimAfterLeaseExpiryAsync()
        {
            CreatePackage(hasRuntime: true);
            var operation = await DeploymentOperationTestData.CreateAsync(
                Db,
                AppId,
                OwnerId,
                DeploymentOperationStatuses.Running,
                PackagePath,
                Target.Slug,
                executorInstanceId: "crashed-api-instance",
                executionAttemptId: Guid.NewGuid(),
                leaseExpiresAtUtc: DateTime.UtcNow.AddMinutes(5));
            var operations = CreateOperationService(
                Service,
                NullLogger<DeploymentOperationService>.Instance);
            var abandonedAttemptId = operation.ExecutionAttemptId;

            Assert.ThrowsAsync<DeploymentOperationLeaseActiveException>(
                async () => await operations.ExecuteAsync(operation.Id, CancellationToken.None));

            operation.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await Db.SaveChangesAsync();
            await operations.ExecuteAsync(operation.Id, CancellationToken.None);

            var result = await Db.DeploymentOperations.SingleAsync();
            return new OperationExecutionSnapshot(
                result.Status,
                result.ExecutorInstanceId != "crashed-api-instance",
                result.ExecutionAttemptId != abandonedAttemptId,
                result.ErrorMessage);
        }

        public async Task<OperationExecutionSnapshot> CancelRunnerExecutionAsync()
        {
            CreatePackage(hasRuntime: true);
            var operation = await DeploymentOperationTestData.CreateAsync(
                Db,
                AppId,
                OwnerId,
                DeploymentOperationStatuses.Pending,
                PackagePath,
                Target.Slug);
            var operations = CreateOperationService(
                Service,
                NullLogger<DeploymentOperationService>.Instance);
            using var cancellation = new CancellationTokenSource();
            var execution = operations.ExecuteAsync(operation.Id, cancellation.Token);

            await RunnerInvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            await execution;

            var result = await Db.DeploymentOperations.SingleAsync();
            return new OperationExecutionSnapshot(
                result.Status,
                false,
                false,
                result.ErrorMessage,
                result.ExecutionAttemptId is not null);
        }

        public sealed record OperationExecutionSnapshot(
            string Status,
            bool Reclaimed,
            bool AttemptChanged,
            string? ErrorMessage,
            bool HasAttempt = false);

        public async Task<AcceptanceSnapshot> AcceptPackageAsync()
        {
            var (operations, publisher) = CreateAcceptanceService();
            var response = await operations.AcceptAsync(
                AppId,
                OwnerId,
                Target.Slug,
                CreatePackage(hasRuntime: true),
                clean: false,
                CancellationToken.None);
            var persisted = await Db.DeploymentOperations.SingleAsync();
            return new AcceptanceSnapshot(
                response.Status,
                response.OperationId,
                persisted.Id,
                File.Exists(persisted.PackagePath),
                publisher.OperationId);
        }

        public async Task AssertIncompatibleRunnerIsRejectedAsync(
            string expectedMessage,
            bool expectedGenericRuntimeDeployed,
            DeploymentTargetStatus expectedStatus)
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Service.DeployPackageAsync(
                    AppId,
                    OwnerId,
                    Target.Slug,
                    CreatePackage(hasRuntime: true),
                    clean: true,
                    CancellationToken.None));
            var target = await Db.AppDeploymentTargets.SingleAsync(item => item.Id == Target.Id);

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain(expectedMessage));
                Assert.That(RunnerInvocationStarted.Task.IsCompleted, Is.False);
                Assert.That(target.GenericRuntimeDeployed, Is.EqualTo(expectedGenericRuntimeDeployed));
                Assert.That(target.Status, Is.EqualTo(expectedStatus));
            });
        }

        private (DeploymentOperationService Operations, RecordingEventPublisher Publisher) CreateAcceptanceService()
        {
            var environment = new TestWebHostEnvironment { ContentRootPath = rootPath };
            var userContext = new UserContextService(new HttpContextAccessor());
            var authorization = new ResourceAuthorizationService(Db, userContext);
            var publisher = new RecordingEventPublisher();
            var operations = new DeploymentOperationService(
                Db,
                authorization,
                new ImmediateTransactionExecutor(),
                publisher,
                new DeploymentPackageStore(environment),
                null!,
                ApiIdentity,
                null!,
                null!);
            return (operations, publisher);
        }

        public sealed record AcceptanceSnapshot(
            string Status,
            Guid ResponseOperationId,
            Guid PersistedOperationId,
            bool PackageExists,
            Guid PublishedOperationId);

        public IFormFile CreatePackage(bool hasRuntime, IReadOnlyDictionary<string, string>? sourceFiles = null)
        {
            var contract = new ApplicationContract();
            if (hasRuntime)
            {
                contract.Runtime.Image = "test/runtime:1";
                contract.Runtime.Command = new ApplicationCommandSpec
                {
                    Executable = "server",
                    WorkingDirectory = "."
                };
            }

            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = archive.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(manifest.Open()))
                {
                    writer.Write($"{{\"version\":1,\"appId\":\"{AppId}\",\"application\":{ApplicationContractSerialization.Serialize(contract)}}}");
                }
                if (sourceFiles is not null)
                {
                    foreach (var sourceFile in sourceFiles)
                    {
                        var sourceEntry = archive.CreateEntry($"source/{sourceFile.Key}");
                        using (var sourceWriter = new StreamWriter(sourceEntry.Open()))
                        {
                            sourceWriter.Write(sourceFile.Value);
                        }
                    }
                }
            }

            var bytes = stream.ToArray();
            File.WriteAllBytes(packagePath, bytes);
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "package.zip");
        }

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
