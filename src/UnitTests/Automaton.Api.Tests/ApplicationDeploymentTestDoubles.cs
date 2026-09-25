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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Automaton.Api.Hubs.AutomatonHub;

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

internal sealed class FixedTargetResolver(AppDeploymentTarget target) : IAppDeploymentTargetResolver
{
    public Task<AppDeploymentTarget> ResolveAsync(Guid appId, string? targetSlug, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(target);

    public Task<AppDeploymentTarget> ResolveByIdAsync(Guid appId, Guid deploymentTargetId, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(target);
}

internal sealed class PackageHubContext : IHubContext<AutomatonHub>
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

internal sealed class PackageHubClients : IHubClients
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

internal sealed class PackageClientProxy(
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
