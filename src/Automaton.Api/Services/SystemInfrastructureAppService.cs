using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class SystemInfrastructureAppService
{
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
    public static readonly Guid AppId = Guid.Parse("7a33f453-f04a-4d17-b767-8c94d9593d30");
    public const string AppName = "Automaton Infrastructure";
    public const string RunnerInfoActionKey = "infrastructure-runner-info";

    private readonly ApplicationDbContext db;
    private readonly IHubContext<AutomatonHub> hub;
    private readonly IAuditEventService audit;
    private readonly string sourceRoot;

    public SystemInfrastructureAppService(ApplicationDbContext db, IHubContext<AutomatonHub> hub, IAuditEventService audit)
    {
        this.db = db;
        this.hub = hub;
        this.audit = audit;
        sourceRoot = Path.Combine(AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure");
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (await db.WebApps.AnyAsync(app => app.Id == AppId, cancellationToken)) return;
        db.WebApps.Add(new WebApp
        {
            Id = AppId, Name = AppName, Summary = "Platform-owned infrastructure Action modules.",
            Description = "Internal Automaton infrastructure operations.", Definition = "{}",
            IsPublished = false, IsForkable = false, RequiresAuthentication = true,
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AppModuleExecution> ExecuteRunnerInfoAsync(Guid runnerId, Guid requestedByUserId, CancellationToken cancellationToken)
    {
        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
        var runner = await db.Runners.AsNoTracking().SingleOrDefaultAsync(item => item.Id == runnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Infrastructure Runner was not found.");
        if (!string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.Ordinal))
            throw new InvalidOperationException("System infrastructure Actions may only run on Infrastructure Runners.");
        if (string.IsNullOrWhiteSpace(runner.ConnectionId)) throw new InvalidOperationException("Infrastructure Runner is not connected.");

        var actionDirectory = Path.Combine(sourceRoot, "actions", RunnerInfoActionKey);
        var manifest = AppActionManifestParser.ParseFile(Path.Combine(actionDirectory, "action.yaml"), RunnerInfoActionKey, allowPrivilegedCapabilities: true);
        var sourceHash = ComputeContentHash(actionDirectory);
        var sourceZip = CreateZip(actionDirectory);
        var preparation = await hub.Clients.Client(runner.ConnectionId).InvokeAsync<AppActionDeploymentResult>(
            AutomatonHubMethods.PrepareAppActionModule,
            new PrepareAppActionModuleRequest(AppId, RunnerInfoActionKey, sourceZip, sourceHash, SystemInfrastructure: true),
            cancellationToken);
        if (preparation.Status is not ("deployed" or "unchanged"))
            throw new InvalidOperationException(preparation.Message ?? "System Action preparation failed.");

        var execution = new AppModuleExecution
        {
            Id = Guid.NewGuid(), AppId = AppId, ModuleType = AppModuleTypes.Action, ModuleKey = RunnerInfoActionKey,
            RunnerId = runner.Id, TriggerType = AppModuleTriggerTypes.Internal, RequestedByUserId = requestedByUserId,
            Status = AppModuleExecutionStatuses.Running, DeploymentHash = sourceHash,
            CreatedAtUtc = DateTime.UtcNow, StartedAtUtc = DateTime.UtcNow
        };
        db.AppModuleExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(execution, AuditActions.Requested, AuditEventTypes.SystemAppActionExecutionRequested, "Infrastructure Action execution requested.", "Success", cancellationToken);

        try
        {
            using var inputDocument = JsonDocument.Parse("{}");
            var result = await hub.Clients.Client(runner.ConnectionId).InvokeAsync<AppActionExecutionResult>(
                AutomatonHubMethods.ExecuteAppActionModule,
                new ExecuteAppActionModuleRequest
                {
                    ExecutionId = execution.Id, AppId = AppId, ActionKey = RunnerInfoActionKey,
                    ExpectedDeploymentHash = sourceHash, Input = inputDocument.RootElement.Clone()
                }, cancellationToken);
            execution.Status = result.Status; execution.ExitCode = result.ExitCode; execution.ErrorCode = result.ErrorCode;
            execution.FailureSummary = result.FailureSummary; execution.CompletedAtUtc = result.CompletedAtUtc;
            execution.LogsAvailableUntilUtc = result.LogsExpireAtUtc; execution.ResultAvailableUntilUtc = result.ResultExpireAtUtc;
            await db.SaveChangesAsync(CancellationToken.None);
            await WriteAuditAsync(execution,
                result.Status == AppModuleExecutionStatuses.Succeeded ? AuditActions.Succeeded : AuditActions.Failed,
                result.Status == AppModuleExecutionStatuses.Succeeded ? AuditEventTypes.SystemAppActionExecutionSucceeded : AuditEventTypes.SystemAppActionExecutionFailed,
                result.Status == AppModuleExecutionStatuses.Succeeded ? "Infrastructure Action execution succeeded." : "Infrastructure Action execution failed.",
                result.Status == AppModuleExecutionStatuses.Succeeded ? "Success" : "Failure", CancellationToken.None);
            return execution;
        }
        catch (Exception ex)
        {
            execution.Status = AppModuleExecutionStatuses.Failed;
            execution.ErrorCode = AppModuleExecutionErrorCodes.PlatformError;
            execution.FailureSummary = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            execution.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await WriteAuditAsync(execution, AuditActions.Failed, AuditEventTypes.SystemAppActionExecutionFailed,
                "Infrastructure Action execution failed.", "Failure", CancellationToken.None);
            return execution;
        }
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private Task WriteAuditAsync(AppModuleExecution execution, string action, string eventType, string message, string outcome, CancellationToken cancellationToken)
        => audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = action, EventType = eventType, EntityType = AuditEntityTypes.AppModuleExecution,
            EntityId = execution.Id, Message = message, Outcome = outcome,
            Metadata = new { execution.AppId, execution.ModuleKey, execution.RunnerId, execution.Status, execution.ErrorCode }
        }, cancellationToken);

    private static byte[] CreateZip(string directory)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                archive.CreateEntryFromFile(file, Path.GetRelativePath(directory, file).Replace('\\', '/'), CompressionLevel.Optimal);
        }
        return stream.ToArray();
    }

    private static string ComputeContentHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/')));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
        }
        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }
}
