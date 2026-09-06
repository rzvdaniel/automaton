using System.IO.Compression;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Automaton.Api.Hubs.AutomatonHub;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ApplicationDeploymentServiceTests
{
    [Test]
    public async Task AcceptingPackage_PersistsOperationBeforeReturning()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var routesPath = Path.GetDirectoryName(fixture.RouteFilePath)!;
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Directory.GetParent(routesPath)!.FullName
        };
        var userContext = new UserContextService(new HttpContextAccessor());
        var authorization = new ResourceAuthorizationService(fixture.Db, userContext);
        var publisher = new RecordingEventPublisher();
        var operations = new DeploymentOperationService(
            fixture.Db,
            authorization,
            new ImmediateTransactionExecutor(),
            publisher,
            environment,
            null!,
            new ApiInstanceIdentityService(),
            null!,
            null!);

        var response = await operations.AcceptAsync(
            fixture.AppId,
            fixture.OwnerId,
            fixture.Target.Slug,
            fixture.CreatePackage(hasRuntime: true),
            clean: false,
            CancellationToken.None);
        var operation = await fixture.Db.DeploymentOperations.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(DeploymentOperationStatuses.Pending));
            Assert.That(operation.Id, Is.EqualTo(response.OperationId));
            Assert.That(File.Exists(operation.PackagePath), Is.True);
            Assert.That(publisher.OperationId, Is.EqualTo(response.OperationId));
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
        var operation = new DeploymentOperation
        {
            Id = Guid.NewGuid(),
            WebAppId = fixture.AppId,
            OwnerUserId = fixture.OwnerId,
            Status = DeploymentOperationStatuses.Running,
            PackagePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-deployment.zip"),
            ExecutorInstanceId = "previous-api-instance",
            LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1),
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        fixture.Db.DeploymentOperations.Add(operation);
        await fixture.Db.SaveChangesAsync();

        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Directory.GetParent(Path.GetDirectoryName(fixture.RouteFilePath)!)!.FullName
        };
        var operations = new DeploymentOperationService(
            fixture.Db,
            null!,
            null!,
            null!,
            environment,
            null!,
            new ApiInstanceIdentityService(),
            null!,
            null!);

        await operations.ExecuteAsync(operation.Id, CancellationToken.None);
        var completed = await fixture.Db.DeploymentOperations.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(completed.Status, Is.EqualTo(DeploymentOperationStatuses.Failed));
            Assert.That(completed.ExecutorInstanceId, Is.Not.EqualTo("previous-api-instance"));
            Assert.That(completed.ErrorMessage, Does.Contain("missing-deployment.zip"));
        });
    }

    [Test]
    public async Task LiveExecutionLease_PreventsDuplicateExecutionFromStealingOperation()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var operation = new DeploymentOperation
        {
            Id = Guid.NewGuid(),
            WebAppId = fixture.AppId,
            OwnerUserId = fixture.OwnerId,
            Status = DeploymentOperationStatuses.Running,
            PackagePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-deployment.zip"),
            ExecutorInstanceId = "another-live-api-instance",
            ExecutionAttemptId = Guid.NewGuid(),
            LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            StartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.DeploymentOperations.Add(operation);
        await fixture.Db.SaveChangesAsync();

        var operations = new DeploymentOperationService(
            fixture.Db,
            null!,
            null!,
            null!,
            new TestWebHostEnvironment
            {
                ContentRootPath = Directory.GetParent(Path.GetDirectoryName(fixture.RouteFilePath)!)!.FullName
            },
            null!,
            new ApiInstanceIdentityService(),
            null!,
            null!);

        Assert.ThrowsAsync<DeploymentOperationLeaseActiveException>(
            async () => await operations.ExecuteAsync(operation.Id, CancellationToken.None));
        var unchanged = await fixture.Db.DeploymentOperations.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(unchanged.Status, Is.EqualTo(DeploymentOperationStatuses.Running));
            Assert.That(unchanged.ExecutorInstanceId, Is.EqualTo("another-live-api-instance"));
            Assert.That(unchanged.ExecutionAttemptId, Is.EqualTo(operation.ExecutionAttemptId));
        });
    }

    [Test]
    public async Task RedeliveryAfterLeaseExpiry_ReclaimsOperationWithoutManualStateReset()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        fixture.CreatePackage(hasRuntime: true);
        var operation = new DeploymentOperation
        {
            Id = Guid.NewGuid(),
            WebAppId = fixture.AppId,
            OwnerUserId = fixture.OwnerId,
            TargetSlug = fixture.Target.Slug,
            Status = DeploymentOperationStatuses.Running,
            PackagePath = fixture.PackagePath,
            Clean = false,
            ExecutorInstanceId = "crashed-api-instance",
            ExecutionAttemptId = Guid.NewGuid(),
            LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            StartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.DeploymentOperations.Add(operation);
        await fixture.Db.SaveChangesAsync();

        var operations = new DeploymentOperationService(
            fixture.Db,
            null!,
            null!,
            null!,
            new TestWebHostEnvironment
            {
                ContentRootPath = Directory.GetParent(Path.GetDirectoryName(fixture.RouteFilePath)!)!.FullName
            },
            fixture.Service,
            new ApiInstanceIdentityService(),
            null!,
            NullLogger<DeploymentOperationService>.Instance);
        var abandonedAttemptId = operation.ExecutionAttemptId;

        Assert.ThrowsAsync<DeploymentOperationLeaseActiveException>(
            async () => await operations.ExecuteAsync(operation.Id, CancellationToken.None));

        operation.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync();
        await operations.ExecuteAsync(operation.Id, CancellationToken.None);

        var completed = await fixture.Db.DeploymentOperations.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(completed.Status, Is.EqualTo(DeploymentOperationStatuses.Succeeded));
            Assert.That(completed.ExecutorInstanceId, Is.Not.EqualTo("crashed-api-instance"));
            Assert.That(completed.ExecutionAttemptId, Is.Not.EqualTo(abandonedAttemptId));
        });
    }

    [Test]
    public async Task TerminalOperation_IsNotReexecuted()
    {
        await using var fixture = await DeploymentFixture.CreateAsync();
        var operation = new DeploymentOperation
        {
            Id = Guid.NewGuid(),
            WebAppId = fixture.AppId,
            OwnerUserId = fixture.OwnerId,
            Status = DeploymentOperationStatuses.Succeeded,
            PackagePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-deployment.zip"),
            CompletedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.DeploymentOperations.Add(operation);
        await fixture.Db.SaveChangesAsync();

        var operations = new DeploymentOperationService(
            fixture.Db,
            null!,
            null!,
            null!,
            new TestWebHostEnvironment
            {
                ContentRootPath = Directory.GetParent(Path.GetDirectoryName(fixture.RouteFilePath)!)!.FullName
            },
            null!,
            new ApiInstanceIdentityService(),
            null!,
            null!);

        await operations.ExecuteAsync(operation.Id, CancellationToken.None);

        var unchanged = await fixture.Db.DeploymentOperations.SingleAsync();
        Assert.That(unchanged.Status, Is.EqualTo(DeploymentOperationStatuses.Succeeded));
    }

    [Test]
    public async Task CallerCancellationDuringRunnerExecution_LeavesOperationDurable()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            runnerInvocationDelay: TimeSpan.FromMilliseconds(250));
        fixture.CreatePackage(hasRuntime: true);
        var operation = new DeploymentOperation
        {
            Id = Guid.NewGuid(),
            WebAppId = fixture.AppId,
            OwnerUserId = fixture.OwnerId,
            TargetSlug = fixture.Target.Slug,
            Status = DeploymentOperationStatuses.Pending,
            PackagePath = fixture.PackagePath,
            Clean = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.DeploymentOperations.Add(operation);
        await fixture.Db.SaveChangesAsync();

        var operations = new DeploymentOperationService(
            fixture.Db,
            null!,
            null!,
            null!,
            new TestWebHostEnvironment
            {
                ContentRootPath = Directory.GetParent(Path.GetDirectoryName(fixture.RouteFilePath)!)!.FullName
            },
            fixture.Service,
            new ApiInstanceIdentityService(),
            null!,
            NullLogger<DeploymentOperationService>.Instance);
        using var cancellation = new CancellationTokenSource();
        var execution = operations.ExecuteAsync(operation.Id, cancellation.Token);

        await fixture.RunnerInvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await execution;

        var completed = await fixture.Db.DeploymentOperations.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(completed.Status, Is.EqualTo(DeploymentOperationStatuses.Succeeded));
            Assert.That(completed.ErrorMessage, Is.Null);
            Assert.That(completed.ExecutionAttemptId, Is.Not.Null);
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
            TaskCompletionSource<bool> runnerInvocationStarted)
        {
            Db = db;
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
            TimeSpan? runnerInvocationDelay = null)
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
            var runner = new Runner
            {
                Id = Guid.NewGuid(),
                Name = "Package deployment runner",
                Purpose = RunnerPurposes.Managed,
                ConnectionId = "package-deployment-connection",
                LastHeartbeatAtUtc = DateTime.UtcNow,
                Status = RunnerStatus.Online.ToString(),
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
                db,
                storage,
                hub,
                uploadRegistry,
                null!,
                runnerPolicy,
                resolver,
                new AppDeploymentTargetStateService(db),
                routeService,
                new ConnectedFlowArtifactService(storage, null!, hub));

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
                runnerInvocationStarted);
        }

        public IFormFile CreatePackage(bool hasRuntime, IReadOnlyDictionary<string, string>? sourceFiles = null)
        {
            var contract = new ApplicationContract();
            if (hasRuntime)
            {
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

    private sealed class FixedTargetResolver(AppDeploymentTarget target) : IAppDeploymentTargetResolver
    {
        public Task<AppDeploymentTarget> ResolveAsync(Guid appId, string? targetSlug, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(target);

        public Task<AppDeploymentTarget> ResolveByIdAsync(Guid appId, Guid deploymentTargetId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(target);
    }

    private sealed class PackageHubContext : IHubContext<AutomatonHub>
    {
        public PackageHubContext(
            FileUploadRegistry uploadRegistry,
            string packagePath,
            DeployAppPackageResult runnerResult,
            TaskCompletionSource<bool> runnerInvocationStarted,
            TimeSpan? runnerInvocationDelay)
        {
            RunnerResult = runnerResult;
            Clients = new PackageHubClients(
                uploadRegistry,
                packagePath,
                runnerResult,
                runnerInvocationStarted,
                runnerInvocationDelay);
        }

        public DeployAppPackageResult RunnerResult { get; }
        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class PackageHubClients : IHubClients
    {
        private readonly PackageClientProxy clientProxy;

        public PackageHubClients(
            FileUploadRegistry uploadRegistry,
            string packagePath,
            DeployAppPackageResult runnerResult,
            TaskCompletionSource<bool> runnerInvocationStarted,
            TimeSpan? runnerInvocationDelay)
        {
            clientProxy = new PackageClientProxy(
                uploadRegistry,
                packagePath,
                runnerResult,
                runnerInvocationStarted,
                runnerInvocationDelay);
        }

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public ISingleClientProxy Client(string connectionId) => clientProxy;
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => clientProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class PackageClientProxy(
        FileUploadRegistry uploadRegistry,
        string packagePath,
        DeployAppPackageResult runnerResult,
        TaskCompletionSource<bool> runnerInvocationStarted,
        TimeSpan? runnerInvocationDelay) : ISingleClientProxy
    {
        public async Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == AutomatonHubMethods.DeployAppPackage)
            {
                runnerInvocationStarted.TrySetResult(true);
                if (runnerInvocationDelay is { } delay)
                    await Task.Delay(delay, CancellationToken.None);
                return (T)(object)runnerResult;
            }

            if (method == AutomatonHubMethods.SynchronizeApplicationManagedArtifacts)
            {
                var request = (SynchronizeApplicationManagedArtifactsRequest)args[0]!;
                return (T)(object)new SynchronizeApplicationManagedArtifactsResult(
                    request.AppId,
                    request.DeploymentTargetId,
                    true);
            }

            throw new NotSupportedException($"Unexpected invoke method: {method}");
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == AutomatonHubMethods.ReceiveFileChunk && args.Length > 0 && args[0] is FileUploadChunk chunk && chunk.IsLast)
            {
                uploadRegistry.TryComplete(new FileUploadCompleteMessage
                {
                    SessionId = chunk.SessionId,
                    LocalPath = packagePath
                });
                return Task.CompletedTask;
            }

            if (method == AutomatonHubMethods.ReceiveFileChunk)
                return Task.CompletedTask;

            throw new NotSupportedException($"Unexpected send method: {method}");
        }
    }

    private sealed class ConflictRouteConfigService : BackendRouteConfigService
    {
        private readonly ApplicationDbContext db;
        private readonly Guid targetId;

        public ConflictRouteConfigService(
            ApplicationDbContext db,
            IOptions<PublishedAppsConfig> options,
            IWebHostEnvironment environment,
            Guid targetId)
            : base(db, options, environment, NullLogger<BackendRouteConfigService>.Instance)
        {
            this.db = db;
            this.targetId = targetId;
        }

        public override async Task RefreshAsync(
            CancellationToken cancellationToken = default,
            Guid? pendingGenericRuntimeTargetId = null,
            bool? pendingGenericRuntimeDeployed = null)
        {
            var target = await db.AppDeploymentTargets.SingleAsync(item => item.Id == targetId, cancellationToken);
            target.Status = DeploymentTargetStatus.Running;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("route refresh failed");
        }
    }

    private sealed class RouteThenStateConflictConfigService : BackendRouteConfigService
    {
        private readonly ApplicationDbContext db;
        private readonly Guid targetId;

        public RouteThenStateConflictConfigService(
            ApplicationDbContext db,
            IOptions<PublishedAppsConfig> options,
            IWebHostEnvironment environment,
            Guid targetId)
            : base(db, options, environment, NullLogger<BackendRouteConfigService>.Instance)
        {
            this.db = db;
            this.targetId = targetId;
        }

        public override async Task RefreshAsync(
            CancellationToken cancellationToken = default,
            Guid? pendingGenericRuntimeTargetId = null,
            bool? pendingGenericRuntimeDeployed = null)
        {
            await base.RefreshAsync(
                cancellationToken,
                pendingGenericRuntimeTargetId,
                pendingGenericRuntimeDeployed);

            var target = await db.AppDeploymentTargets.SingleAsync(item => item.Id == targetId, cancellationToken);
            target.Status = DeploymentTargetStatus.Running;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class ImmediateTransactionExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public Guid OperationId { get; private set; }

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : class
        {
            OperationId = ((DeploymentPackageRequestedEvent)(object)message).OperationId;
            return Task.CompletedTask;
        }
    }
}
