using System.IO.Compression;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class DeploymentOperationTestData
{
    public static async Task<DeploymentOperation> CreateAsync(
        ApplicationDbContext db,
        Guid appId,
        Guid ownerId,
        string status,
        string packagePath,
        string? targetSlug = null,
        string? executorInstanceId = null,
        Guid? executionAttemptId = null,
        DateTime? leaseExpiresAtUtc = null,
        DateTime? startedAtUtc = null,
        DateTime? completedAtUtc = null)
    {
        var operation = new DeploymentOperation
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            OwnerUserId = ownerId,
            TargetSlug = targetSlug,
            Status = status,
            PackagePath = packagePath,
            ExecutorInstanceId = executorInstanceId,
            ExecutionAttemptId = executionAttemptId,
            LeaseExpiresAtUtc = leaseExpiresAtUtc,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.DeploymentOperations.Add(operation);
        await db.SaveChangesAsync();
        return operation;
    }
}

internal static class DeploymentPackageArchiveFactory
{
    public static IFormFile Create(
        Guid appId,
        string packagePath,
        bool hasRuntime,
        IReadOnlyDictionary<string, string>? sourceFiles = null)
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
                writer.Write($"{{\"version\":1,\"appId\":\"{appId}\",\"application\":{ApplicationContractSerialization.Serialize(contract)}}}");
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
}

internal sealed record DeploymentFixtureSettings(
    bool InitialGenericMarker,
    bool RouteRootAsFile,
    bool RouteStateConflict,
    bool RouteThenStateConflict,
    DeployAppPackageResult? RunnerResult,
    TimeSpan? RunnerInvocationDelay,
    string? RunnerApiCompatibilityVersion);

internal sealed record DeploymentFixtureWorkspace(string RootPath, string PackagePath, string RouteRoot);

internal sealed record DeploymentFixtureState(
    ApplicationDbContext Db,
    Guid OwnerId,
    Guid AppId,
    ApiInstanceIdentityService ApiIdentity,
    Runner Runner,
    AppDeploymentTarget Target);

internal sealed record DeploymentFixtureServices(
    ApplicationDeploymentService Service,
    DeployAppPackageResult RunnerResult,
    TaskCompletionSource<bool> RunnerInvocationStarted);

internal static class DeploymentFixtureFactory
{
    public static async Task<ApplicationDeploymentServiceTests.DeploymentFixture> CreateAsync(
        DeploymentFixtureSettings settings)
    {
        var workspace = await DeploymentFixtureWorkspaceFactory.CreateAsync(settings.RouteRootAsFile);
        var state = await DeploymentFixtureStateFactory.CreateAsync(workspace, settings);
        var services = DeploymentFixtureServiceFactory.Create(state, workspace, settings);

        return new ApplicationDeploymentServiceTests.DeploymentFixture(
            state.Db,
            services.Service,
            state.OwnerId,
            state.AppId,
            state.Target,
            services.RunnerResult,
            workspace.RootPath,
            workspace.PackagePath,
            settings.RouteRootAsFile,
            services.RunnerInvocationStarted,
            state.ApiIdentity);
    }
}

internal static class DeploymentFixtureWorkspaceFactory
{
    public static async Task<DeploymentFixtureWorkspace> CreateAsync(bool routeRootAsFile)
    {
        var rootPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "package-deploy",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        var packagePath = Path.Combine(rootPath, "package.zip");
        var routeRoot = Path.Combine(rootPath, "routes");
        if (routeRootAsFile)
            await File.WriteAllTextAsync(routeRoot, "route-root-placeholder");

        return new DeploymentFixtureWorkspace(rootPath, packagePath, routeRoot);
    }
}

internal static class DeploymentFixtureStateFactory
{
    public static async Task<DeploymentFixtureState> CreateAsync(
        DeploymentFixtureWorkspace workspace,
        DeploymentFixtureSettings settings)
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"package-deploy-{Guid.NewGuid():N}")
                .Options);

        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiIdentity = new ApiInstanceIdentityService();
        var runner = CreateRunner(apiIdentity, settings.RunnerApiCompatibilityVersion);
        var target = AppDeploymentTarget.CreateDefault(appId, runner.Id);
        target.GenericRuntimeDeployed = settings.InitialGenericMarker;
        target.ExecutionRunner = runner;
        db.WebApps.Add(CreateApp(appId, target));
        db.Runners.Add(runner);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = appId, UserId = ownerId });
        db.HostedRunners.Add(CreateHostedRunner(ownerId, runner.Id));
        await db.SaveChangesAsync();

        return new DeploymentFixtureState(db, ownerId, appId, apiIdentity, runner, target);
    }

    private static Runner CreateRunner(ApiInstanceIdentityService apiIdentity, string? compatibilityVersion)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = "Package deployment runner",
            Purpose = RunnerPurposes.Managed,
            ConnectionId = "package-deployment-connection",
            ApiInstanceId = apiIdentity.ApiInstanceId,
            LastHeartbeatAtUtc = DateTime.UtcNow,
            Status = RunnerStatus.Online.ToString(),
            ApiCompatibilityVersion = compatibilityVersion ?? RunnerApiCompatibilityVersions.Current,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                RunnerPurposes.Managed)
        };

    private static WebApp CreateApp(Guid appId, AppDeploymentTarget target)
        => new()
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

    private static HostedRunner CreateHostedRunner(Guid ownerId, Guid runnerId)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Name = "Package deployment hosted runner",
            ContainerName = "package-deployment-runner",
            Status = HostedRunnerStatuses.Running,
            RunnerId = runnerId,
            RuntimeEndpoint = "http://runner:8080"
        };
}

internal static class DeploymentFixtureServiceFactory
{
    public static DeploymentFixtureServices Create(
        DeploymentFixtureState state,
        DeploymentFixtureWorkspace workspace,
        DeploymentFixtureSettings settings)
    {
        var environment = new TestWebHostEnvironment { ContentRootPath = workspace.RootPath };
        var storage = CreateStorage(state.Db, environment);
        var invocationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transferGateway = CreateTransferGateway(workspace.PackagePath, state.Target.Id, invocationStarted, settings);
        var resolver = new FixedTargetResolver(state.Target);
        var routeService = DeploymentFixtureRouteServiceFactory.Create(state.Db, workspace, environment, state.Target.Id, settings);
        var service = CreateDeploymentService(state, storage, transferGateway, resolver, routeService);

        return new DeploymentFixtureServices(service, transferGateway.RunnerResult, invocationStarted);
    }

    private static ApplicationDeploymentService CreateDeploymentService(
        DeploymentFixtureState state,
        AppFileStorageService storage,
        IRunnerFileTransferGateway transferGateway,
        FixedTargetResolver resolver,
        BackendRouteConfigService routeService)
        => new(
            new DeploymentPackageRunnerService(transferGateway),
            null!,
            new AppRunnerAssignmentPolicy(state.Db, null!, null!, resolver),
            resolver,
            new AppDeploymentTargetStateService(state.Db),
            new DeploymentPackageFinalizationService(
                state.Db,
                storage,
                new ConnectedFlowArtifactService(storage, null!, new PackageManagedArtifactsCommands()),
                routeService));

    internal static AppFileStorageService CreateStorage(ApplicationDbContext db, TestWebHostEnvironment environment)
    {
        var userContext = new UserContextService(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var settings = new SettingsService(db, userContext, Options.Create(new AppFileRuleBaselineOptions()));
        return new AppFileStorageService(environment, settings);
    }

    private static PackageTransferGateway CreateTransferGateway(
        string packagePath,
        Guid targetId,
        TaskCompletionSource<bool> invocationStarted,
        DeploymentFixtureSettings settings)
    {
        var runnerResult = settings.RunnerResult ?? new DeployAppPackageResult
        {
            DeploymentTargetId = targetId,
            Success = true,
            Message = "package deployed"
        };
        return new PackageTransferGateway(
            packagePath,
            runnerResult,
            invocationStarted,
            settings.RunnerInvocationDelay);
    }
}

internal static class DeploymentFixtureRouteServiceFactory
{
    public static BackendRouteConfigService Create(
        ApplicationDbContext db,
        DeploymentFixtureWorkspace workspace,
        TestWebHostEnvironment environment,
        Guid targetId,
        DeploymentFixtureSettings settings)
    {
        var routeOptions = Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = workspace.RouteRoot });
        if (settings.RouteThenStateConflict)
            return new RouteThenStateConflictConfigService(db, routeOptions, environment, targetId);
        if (settings.RouteStateConflict)
            return new ConflictRouteConfigService(db, routeOptions, environment, targetId);

        return new BackendRouteConfigService(db, routeOptions, environment, NullLogger<BackendRouteConfigService>.Instance);
    }
}

internal static class DeploymentPackageTestScenarios
{
    public static async Task AssertIncompatibleRunnerIsRejectedAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture,
        string expectedMessage,
        bool expectedGenericRuntimeDeployed,
        DeploymentTargetStatus expectedStatus)
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                fixture.CreatePackage(hasRuntime: true),
                clean: true,
                CancellationToken.None));
        var target = await DeploymentTargetSnapshot.ReadAsync(fixture.Db, fixture.Target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(expectedMessage));
            Assert.That(fixture.RunnerInvocationStarted.Task.IsCompleted, Is.False);
            Assert.That(target.GenericRuntimeDeployed, Is.EqualTo(expectedGenericRuntimeDeployed));
            Assert.That(target.Status, Is.EqualTo(expectedStatus));
        });
    }

    public static async Task<SuccessfulDeploymentSnapshot> DeployRuntimeAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        var response = await fixture.Service.DeployPackageAsync(
            fixture.AppId,
            fixture.OwnerId,
            fixture.Target.Slug,
            fixture.CreatePackage(hasRuntime: true),
            clean: false,
            CancellationToken.None);
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(item => item.Id == fixture.Target.Id);
        var app = await fixture.Db.WebApps.SingleAsync(item => item.Id == fixture.AppId);

        return new SuccessfulDeploymentSnapshot(
            response.Success,
            target.Status,
            target.GenericRuntimeDeployed,
            target.LastSuccessfulDeploymentAt is not null,
            app.ApplicationContractJson.Contains("server", StringComparison.Ordinal),
            File.Exists(fixture.RouteFilePath),
            File.Exists(fixture.RouteFilePath)
                ? await File.ReadAllTextAsync(fixture.RouteFilePath)
                : string.Empty);
    }

    public static async Task<FailedDeploymentSnapshot> FailFinalizationAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        var exception = Assert.ThrowsAsync<IOException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                fixture.CreatePackage(hasRuntime: true),
                clean: false,
                CancellationToken.None));
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(item => item.Id == fixture.Target.Id);
        return new FailedDeploymentSnapshot(exception!.Message, target.Status, target.GenericRuntimeDeployed, false, string.Empty);
    }

    public static Task<FailedDeploymentSnapshot> FailInitialRouteCompletionAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
        => FailRouteCompletionAsync(fixture, hasRuntime: true);

    public static Task<FailedDeploymentSnapshot> FailNoRuntimeRouteCompletionAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
        => FailRouteCompletionAsync(fixture, hasRuntime: false);

    private static async Task<FailedDeploymentSnapshot> FailRouteCompletionAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture,
        bool hasRuntime)
    {
        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(async () =>
            await fixture.Service.DeployPackageAsync(
                fixture.AppId,
                fixture.OwnerId,
                fixture.Target.Slug,
                fixture.CreatePackage(hasRuntime),
                clean: false,
                CancellationToken.None));
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(item => item.Id == fixture.Target.Id);
        var routeExists = File.Exists(fixture.RouteFilePath);
        return new FailedDeploymentSnapshot(
            exception!.Message,
            target.Status,
            target.GenericRuntimeDeployed,
            routeExists,
            routeExists ? await File.ReadAllTextAsync(fixture.RouteFilePath) : string.Empty);
    }

    public sealed record SuccessfulDeploymentSnapshot(
        bool Succeeded,
        DeploymentTargetStatus TargetStatus,
        bool GenericRuntimeDeployed,
        bool HasSuccessfulDeploymentTime,
        bool ApplicationContractHasServer,
        bool RouteExists,
        string RouteContent);

    public sealed record FailedDeploymentSnapshot(
        string ErrorMessage,
        DeploymentTargetStatus TargetStatus,
        bool GenericRuntimeDeployed,
        bool RouteExists,
        string RouteContent);
}

internal sealed record DeploymentTargetSnapshot(DeploymentTargetStatus Status, bool GenericRuntimeDeployed)
{
    public static async Task<DeploymentTargetSnapshot> ReadAsync(ApplicationDbContext db, Guid targetId)
    {
        var target = await db.AppDeploymentTargets.SingleAsync(item => item.Id == targetId);
        return new DeploymentTargetSnapshot(target.Status, target.GenericRuntimeDeployed);
    }
}

internal static class DeploymentOperationTestScenarios
{
    public static DeploymentOperationService CreateService(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture,
        ApplicationDeploymentService? deploymentService = null)
        => new(
            fixture.Db,
            null!,
            null!,
            null!,
            new DeploymentPackageStore(
                new TestWebHostEnvironment { ContentRootPath = fixture.RootPath },
                Options.Create(new DeploymentPackageStorageOptions())),
            deploymentService ?? fixture.Service,
            new NoHostedRunnerCommandDispatcher(),
            fixture.ApiIdentity,
            null!,
            NullLogger<DeploymentOperationService>.Instance);

    public static Task<List<Guid>> GetDispatchableOperationIdsAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
        => CreateService(fixture).GetDispatchableOwnedOperationIdsAsync(
            10,
            Array.Empty<Guid>(),
            CancellationToken.None);

    public static async Task<OperationExecutionSnapshot> ReclaimExpiredOperationAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        var operation = await DeploymentOperationTestData.CreateAsync(
            fixture.Db,
            fixture.AppId,
            fixture.OwnerId,
            DeploymentOperationStatuses.Running,
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-deployment.zip"),
            executorInstanceId: "previous-api-instance",
            leaseExpiresAtUtc: DateTime.UtcNow.AddSeconds(-1),
            startedAtUtc: DateTime.UtcNow.AddMinutes(-1));

        await CreateService(fixture).ExecuteAsync(operation.Id, CancellationToken.None);
        var result = await fixture.Db.DeploymentOperations.SingleAsync();
        return new OperationExecutionSnapshot(
            result.Status,
            result.ExecutorInstanceId != "previous-api-instance",
            false,
            result.ErrorMessage);
    }

    public static async Task<OperationExecutionSnapshot> ReclaimAfterLeaseExpiryAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        fixture.CreatePackage(hasRuntime: true);
        var operation = await DeploymentOperationTestData.CreateAsync(
            fixture.Db,
            fixture.AppId,
            fixture.OwnerId,
            DeploymentOperationStatuses.Running,
            fixture.PackagePath,
            fixture.Target.Slug,
            executorInstanceId: "crashed-api-instance",
            executionAttemptId: Guid.NewGuid(),
            leaseExpiresAtUtc: DateTime.UtcNow.AddMinutes(5));
        var operations = CreateService(fixture);
        var abandonedAttemptId = operation.ExecutionAttemptId;

        Assert.ThrowsAsync<DeploymentOperationLeaseActiveException>(
            async () => await operations.ExecuteAsync(operation.Id, CancellationToken.None));

        operation.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync();
        await operations.ExecuteAsync(operation.Id, CancellationToken.None);

        var result = await fixture.Db.DeploymentOperations.SingleAsync();
        return new OperationExecutionSnapshot(
            result.Status,
            result.ExecutorInstanceId != "crashed-api-instance",
            result.ExecutionAttemptId != abandonedAttemptId,
            result.ErrorMessage);
    }

    public static async Task<OperationExecutionSnapshot> CancelRunnerExecutionAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        fixture.CreatePackage(hasRuntime: true);
        var operation = await DeploymentOperationTestData.CreateAsync(
            fixture.Db,
            fixture.AppId,
            fixture.OwnerId,
            DeploymentOperationStatuses.Pending,
            fixture.PackagePath,
            fixture.Target.Slug);
        var operations = CreateService(fixture);
        using var cancellation = new CancellationTokenSource();
        var execution = operations.ExecuteAsync(operation.Id, cancellation.Token);

        await fixture.RunnerInvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await execution;

        var result = await fixture.Db.DeploymentOperations.SingleAsync();
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
}

internal static class DeploymentAcceptanceTestScenarios
{
    public static async Task<AcceptanceSnapshot> AcceptPackageAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        var service = DeploymentAcceptanceServiceFactory.Create(fixture);
        var response = await service.Operations.AcceptAsync(
            fixture.AppId,
            fixture.OwnerId,
            fixture.Target.Slug,
            fixture.CreatePackage(hasRuntime: true),
            clean: false,
            CancellationToken.None);
        var persisted = await fixture.Db.DeploymentOperations.SingleAsync();
        return new AcceptanceSnapshot(
            response.Status,
            response.OperationId,
            persisted.Id,
            File.Exists(persisted.PackagePath),
            service.Publisher.OperationId);
    }

    public sealed record AcceptanceSnapshot(
        string Status,
        Guid ResponseOperationId,
        Guid PersistedOperationId,
        bool PackageExists,
        Guid PublishedOperationId);
}

internal static class DeploymentAcceptanceServiceFactory
{
    public static DeploymentAcceptanceService Create(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        var environment = new TestWebHostEnvironment { ContentRootPath = fixture.RootPath };
        var userContext = new UserContextService(new HttpContextAccessor());
        var authorization = new ResourceAuthorizationService(fixture.Db, userContext);
        var publisher = new RecordingEventPublisher();
        var operations = new DeploymentOperationService(
            fixture.Db,
            authorization,
            new ImmediateTransactionExecutor(),
            publisher,
            new DeploymentPackageStore(environment, Options.Create(new DeploymentPackageStorageOptions())),
            null!,
            new NoHostedRunnerCommandDispatcher(),
            fixture.ApiIdentity,
            null!,
            null!);
        return new DeploymentAcceptanceService(operations, publisher);
    }
}

internal sealed record DeploymentAcceptanceService(
    DeploymentOperationService Operations,
    RecordingEventPublisher Publisher);

internal sealed class NoHostedRunnerCommandDispatcher : IHostedRunnerDeploymentCommandDispatcher
{
    public Task<bool> PublishAsync(DeploymentOperation operation, Guid executionAttemptId, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

internal sealed class FixedTargetResolver(AppDeploymentTarget target) : IAppDeploymentTargetResolver
{
    public Task<AppDeploymentTarget> ResolveAsync(Guid appId, string? targetSlug, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(target);

    public Task<AppDeploymentTarget> ResolveByIdAsync(Guid appId, Guid deploymentTargetId, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(target);
}

internal sealed class PackageTransferGateway(
    string packagePath, DeployAppPackageResult runnerResult,
    TaskCompletionSource<bool> invocationStarted, TimeSpan? invocationDelay) : IRunnerFileTransferGateway
{
    public async Task<(Guid OperationId, RunnerExecutionResult Result)> ExecuteAsync(
        Guid operationId, Guid runnerId, Guid ownerId, RunnerFileTransferCommand command,
        IFormFile? input, CancellationToken token, TimeSpan? timeout = null)
    {
        Assert.That(input, Is.Not.Null);
        invocationStarted.TrySetResult(true);
        if (invocationDelay is { } delay) await Task.Delay(delay, CancellationToken.None);
        var deployment = new RunnerFileTransferResult
        {
            Success = runnerResult.Success,
            Error = runnerResult.Message,
            Deployment = runnerResult
        };
        return (operationId, new RunnerExecutionResult
        {
            Outcome = runnerResult.Success ? "Succeeded" : "Failed",
            Error = runnerResult.Success ? null : runnerResult.Message,
            FileTransferResult = deployment
        });
    }

    public Stream OpenOutput(Guid operationId) => File.OpenRead(packagePath);

    public Task CleanupOutputAsync(Guid operationId) => Task.CompletedTask;
    public DeployAppPackageResult RunnerResult { get; } = runnerResult;
}

internal sealed class ConflictRouteConfigService : BackendRouteConfigService
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

internal sealed class RouteThenStateConflictConfigService : BackendRouteConfigService
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

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Automaton.Api.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

internal sealed class ImmediateTransactionExecutor : ITransactionalEventExecutor
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
        operation(cancellationToken);
}

internal sealed class RecordingEventPublisher : IEventPublisher
{
    public Guid OperationId { get; private set; }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        OperationId = ((DeploymentPackageRequestedEvent)(object)message).OperationId;
        return Task.CompletedTask;
    }
}
