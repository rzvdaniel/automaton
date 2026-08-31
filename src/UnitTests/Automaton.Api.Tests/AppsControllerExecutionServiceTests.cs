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
public sealed class AppsControllerExecutionServiceTests
{
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
            Assert.That(target.LegacyControllerRuntimeDeployed, Is.False);
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
            initialLegacyMarker: true,
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
            Assert.That(target.LegacyControllerRuntimeDeployed, Is.True);
            Assert.That(target.GenericRuntimeDeployed, Is.True);
        });
    }

    [Test]
    public async Task DeployPackageAsync_FinalizationFailurePreservesOriginalErrorAndDoesNotClaimLegacyRuntime()
    {
        await using var fixture = await DeploymentFixture.CreateAsync(
            initialLegacyMarker: false,
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
            Assert.That(target.LegacyControllerRuntimeDeployed, Is.False);
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

    private sealed class DeploymentFixture : IAsyncDisposable
    {
        private readonly string rootPath;
        private readonly string packagePath;
        private readonly bool routeRootAsFile;

        public ApplicationDbContext Db { get; }
        public AppsControllerExecutionService Service { get; }
        public Guid OwnerId { get; }
        public Guid AppId { get; }
        public AppDeploymentTarget Target { get; }
        public DeployAppPackageResult RunnerResult { get; }
        public string RouteFilePath => Path.Combine(rootPath, "routes", "managed-runtime-routes.yml");

        private DeploymentFixture(
            ApplicationDbContext db,
            AppsControllerExecutionService service,
            Guid ownerId,
            Guid appId,
            AppDeploymentTarget target,
            DeployAppPackageResult runnerResult,
            string rootPath,
            string packagePath,
            bool routeRootAsFile)
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
        }

        public static async Task<DeploymentFixture> CreateAsync(
            bool initialLegacyMarker = false,
            bool initialGenericMarker = false,
            bool routeRootAsFile = false,
            bool routeStateConflict = false,
            DeployAppPackageResult? runnerResult = null)
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
            target.LegacyControllerRuntimeDeployed = initialLegacyMarker;
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
            var hub = new PackageHubContext(uploadRegistry, packagePath, runnerResult ?? new DeployAppPackageResult
            {
                DeploymentTargetId = target.Id,
                Success = true,
                Message = "package deployed"
            });
            var resolver = new FixedTargetResolver(target);
            var routeOptions = Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = routeRoot });
            var routeService = routeStateConflict
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
            var service = new AppsControllerExecutionService(
                db,
                storage,
                hub,
                new TunnelRequestRegistry(),
                uploadRegistry,
                Options.Create(new AppsTunnelConfig()),
                new ApiInstanceIdentityService(),
                new AppDeployCapabilityService(storage),
                null!,
                routeService,
                null!,
                runnerPolicy,
                resolver,
                new AppDeploymentTargetStateService(db));

            return new DeploymentFixture(db, service, ownerId, appId, target, hub.RunnerResult, rootPath, packagePath, routeRootAsFile);
        }

        public IFormFile CreatePackage(bool hasRuntime)
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
                using var writer = new StreamWriter(manifest.Open());
                writer.Write($"{{\"version\":1,\"appId\":\"{AppId}\",\"application\":{ApplicationContractSerialization.Serialize(contract)}}}");
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
        public PackageHubContext(FileUploadRegistry uploadRegistry, string packagePath, DeployAppPackageResult runnerResult)
        {
            RunnerResult = runnerResult;
            Clients = new PackageHubClients(uploadRegistry, packagePath, runnerResult);
        }

        public DeployAppPackageResult RunnerResult { get; }
        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class PackageHubClients(FileUploadRegistry uploadRegistry, string packagePath, DeployAppPackageResult runnerResult) : IHubClients
    {
        private readonly PackageClientProxy clientProxy = new(uploadRegistry, packagePath, runnerResult);

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
        DeployAppPackageResult runnerResult) : ISingleClientProxy
    {
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == AutomatonHubMethods.DeployAppPackage)
                return Task.FromResult((T)(object)runnerResult);

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

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
