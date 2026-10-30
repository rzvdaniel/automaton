using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Core.Scripting.Engines;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Automaton.Api.Models;
using Automaton.Api.Enums;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Entities;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Mapping;

namespace Automaton.Api.Services;

public class RunnerService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;  // ✅ Store service, not userId
    private readonly ResourceAuthorizationService authorizationService;
    private readonly RunnerDeletionService runnerDeletionService;
    private readonly RunnerConnectionSessionService connectionSessionService;
    private readonly RunnerQueryService runnerQueryService;
    private readonly RunnerFlowExecutionService flowExecutionService;
    private readonly RunnerConnectionLifecycleService connectionLifecycleService;
    private readonly Serilog.ILogger logger;

    // ✅ Helper property to get current userId (respects impersonation)
    private Guid UserId => userContextService.GetUserId();

    public RunnerService(ApplicationDbContext dbContext,
        UserContextService userContextService,
        ResourceAuthorizationService authorizationService,
        RunnerDeletionService runnerDeletionService,
        RunnerConnectionSessionService connectionSessionService,
        RunnerQueryService runnerQueryService,
        RunnerFlowExecutionService flowExecutionService,
        RunnerConnectionLifecycleService connectionLifecycleService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;  // ✅ Store service reference
        this.authorizationService = authorizationService;
        this.runnerDeletionService = runnerDeletionService;
        this.connectionSessionService = connectionSessionService;
        this.runnerQueryService = runnerQueryService;
        this.flowExecutionService = flowExecutionService;
        this.connectionLifecycleService = connectionLifecycleService;
        logger = Serilog.Log.ForContext<RunnerService>();
    }

    public Task<IEnumerable<RunnerDetails>> List(CancellationToken cancellationToken)
        => runnerQueryService.List(cancellationToken);

    public Task<IEnumerable<RunnerDetails>> List(Guid? projectId, CancellationToken cancellationToken)
        => runnerQueryService.List(projectId, cancellationToken);

    public Runner Get(Guid id)
        => runnerQueryService.Get(id);
    public Task UpdateConnection(
        Guid runnerId,
        Guid userId,
        RunnerDetails runnerConnection,
        CancellationToken cancellationToken)
        => connectionLifecycleService.UpdateConnectionAsync(runnerId, userId, runnerConnection, cancellationToken);

    public Task RecordHeartbeatAsync(
        Guid runnerId,
        Guid userId,
        RunnerDetails runnerHeartbeat,
        CancellationToken cancellationToken)
        => connectionLifecycleService.RecordHeartbeatAsync(runnerId, userId, runnerHeartbeat, cancellationToken);

    internal static void ApplyInstallationReport(Runner runner, RunnerDetails report)
        => RunnerConnectionLifecycleService.ApplyInstallationReport(runner, report);
    public Task MarkDisconnectedAsync(Guid runnerId, string connectionId, CancellationToken cancellationToken)
        => connectionSessionService.MarkDisconnectedAsync(runnerId, connectionId, cancellationToken);

    public Task<int> CloseStaleConnectionSessionsAsync(CancellationToken cancellationToken)
        => connectionSessionService.CloseStaleConnectionSessionsAsync(cancellationToken);
    public async Task Update(Guid id, UpdateRunnerRequest runner, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(id, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {id}");
        }

        var entity = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"Runner {id} not found");
        }

        if (runner.InstallationId is not null && runner.InstallationId != entity.InstallationId)
            throw new ArgumentException("Runner installation identity cannot be changed through the update API.", nameof(runner));

        var capabilities = RunnerCapabilities.Normalize(runner.Capabilities, entity.Purpose);

        entity.Name = runner.Name;
        entity.Version = runner.Version;
        entity.MachineName = runner.MachineName;
        entity.IpAddress = runner.IpAddress;
        entity.CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, entity.Purpose);

        // Mark entity as modified
        dbContext.Entry(entity).State = EntityState.Modified;

        // Update runner entity
        dbContext.Update(entity);

        // Save changes
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task Update(Guid id, RunnerDetails runner, CancellationToken cancellationToken)
        => Update(id, new UpdateRunnerRequest
        {
            Name = runner.Name,
            Version = runner.Version,
            MachineName = runner.MachineName,
            IpAddress = runner.IpAddress,
            Capabilities = runner.Capabilities,
            InstallationId = runner.InstallationId
        }, cancellationToken);

    public bool Exists(string name)
    {
        var exists = dbContext.Runners.Any(x =>
            x.Name.ToLower() == name.ToLower() &&
            x.RunnerUsers.Any(x => x.UserId == UserId));

        return exists;
    }

    public bool ExistsById(Guid id)
    {
        var exists = dbContext.Runners.Any(x =>
         x.Id == id &&
            x.RunnerUsers.Any(x => x.UserId == UserId));

        return exists;
    }

    public Task Delete(Guid id, CancellationToken cancellationToken, bool hostCleanupConfirmed = false)
        => runnerDeletionService.DeleteAsync(id, cancellationToken, hostCleanupConfirmed);

    public Task<IEnumerable<RunnerFlowResult>> ExecuteFlow(
        Guid flowId,
        IEnumerable<Guid> runnerIds,
        CancellationToken cancellationToken)
        => flowExecutionService.ExecuteAsync(flowId, runnerIds, cancellationToken);

    public Task<IEnumerable<RunnerFlowResult>> ExecuteFlow(
        Guid flowId,
        IEnumerable<Guid> runnerIds,
        Guid userId,
        CancellationToken cancellationToken)
        => flowExecutionService.ExecuteAsync(flowId, runnerIds, userId, cancellationToken);
    /// <summary>
    /// Get script engine settings for a specific runner
    /// </summary>
    public async Task<RunnerScriptEngineSettings?> GetScriptEngineSettings(Guid id, CancellationToken cancellationToken)
    {
        // Check if user can access this runner (personal owner OR project member)
        if (!await authorizationService.CanAccessRunnerAsync(id, cancellationToken))
        {
            return null;
        }

        var runner = await dbContext.Runners
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (runner == null || string.IsNullOrWhiteSpace(runner.ScriptEngineSettings))
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<RunnerScriptEngineSettings>(
                runner.ScriptEngineSettings,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                   );

            return settings;
        }
        catch (JsonException ex)
        {
            logger.Warning(ex, "Failed to deserialize script engine settings for runner {RunnerId}", id);
            return null;
        }
    }

    /// <summary>
    /// Update script engine settings for a specific runner
    /// </summary>
    public async Task UpdateScriptEngineSettings(Guid id, string? settingsJson, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(id, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {id}");
        }

        var runner = await dbContext.Runners
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (runner == null)
        {
            throw new InvalidOperationException($"Runner {id} not found");
        }

        runner.ScriptEngineSettings = settingsJson;

        dbContext.Entry(runner).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Debug("Updated script engine settings for runner {RunnerId}", id);
    }

    public Task<RunnerDetails?> GetByNameAsync(string name, CancellationToken cancellationToken)
        => runnerQueryService.GetByNameAsync(name, cancellationToken);
}
