using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;
using Automaton.Api.Models;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Core.Enums;

namespace Automaton.Api.Services;

public class FlowsService
{
    private readonly ApplicationDbContext dbContext;
    private readonly RunnerService runnerService;
    private readonly ScheduleService scheduleService;
    private readonly RecentActivityService recentActivityService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly SensitiveVariableService sensitiveVariableService;
    private readonly Guid userId;
    private readonly Serilog.ILogger logger;

    public FlowsService
    (
        ApplicationDbContext dbContext,
        RunnerService runnerService,
        ScheduleService scheduleService,
        RecentActivityService recentActivityService,
        UserContextService userContextService,
        ResourceAuthorizationService authorizationService,
        SensitiveVariableService sensitiveVariableService
    )
    {
        this.dbContext = dbContext;
        this.runnerService = runnerService;
        this.scheduleService = scheduleService;
        this.recentActivityService = recentActivityService;
        this.authorizationService = authorizationService;
        this.sensitiveVariableService = sensitiveVariableService;
        this.userId = userContextService.GetUserId();
        this.logger = Log.ForContext<FlowsService>();
    }

    public IEnumerable<FlowInfo> List(Guid? projectId = null)
    {
        // Use authorization service to get flows filtered by project context
        var accessibleFlows = authorizationService.GetFlowsQueryByContext(projectId);

        var flows =
            from m in accessibleFlows
                .Include(m => m.FlowUsers)
            from s in dbContext.FlowExecutions
                .Where(s => m.Id == s.FlowId)
                .DefaultIfEmpty()
                .OrderByDescending(x => x.Started)
                .Take(1)    
            select new FlowInfo
            {
                Id = m.Id,
                Created = m.Created,
                Updated = m.Updated,
                Name = m.Name,
                Started = s != null ? s.Started : DateTime.MinValue,
                Finished = s != null ? s.Finished : DateTime.MinValue,
                Status = s != null ? s.Status : WorkflowStatus.None.ToString()
            };

        return flows;
    }

    public Flow Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var entity = authorizationService.GetAccessibleFlowsQuery()
            .SingleOrDefault(x => x.Id == id);

        var flow = DeserializeFlow(entity.Body);

        // ✅ Decrypt sensitive variables for both UI and Runner execution
        // Security is handled by: authorization checks, password input masking, and encryption at rest
        DecryptSensitiveVariablesAsync(flow).Wait();

        return flow;
    }

    public async Task<Guid> Create(Flow flow)
    {
        flow.Id = Guid.NewGuid();
        flow.Created = DateTime.UtcNow;
        flow.Updated = DateTime.UtcNow;

        // ✅ Process sensitive variables BEFORE serialization
        await ProcessSensitiveVariablesAsync(flow);

        var flowEntity = new Entities.Flow
        {
            Id = flow.Id,
            Name = flow.Name,
            Body = JsonSerializer.Serialize(flow),
            Created = flow.Created,
            Updated = flow.Updated
        };

        dbContext.Flows.Add(flowEntity);

        var flowUser = new Entities.FlowUser
        {
            FlowId = flowEntity.Id,
            UserId = userId
        };

        dbContext.FlowUsers.Add(flowUser);

        dbContext.SaveChanges();

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowCreated, flow.Name, flow.Id);

        return flowEntity.Id;
    }

    public async Task Update(Guid id, Flow flow)
    {
        // Check if user can update this flow (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateFlowAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update flow {id}");
        }

        var entity = dbContext.Flows.SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"Flow {id} not found");
        }

        // ✅ Process sensitive variables BEFORE serialization
        await ProcessSensitiveVariablesAsync(flow);

        entity.Name = flow.Name;
        entity.Body = JsonSerializer.Serialize(flow);
        entity.Updated = DateTime.UtcNow;

        dbContext.SaveChanges();

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowUpdated, flow.Name, id);
    }

    public async Task Remove(Guid id)
    {
        // Check if user can delete this flow (must be personal owner AND not part of any project)
        if (!await authorizationService.CanDeleteFlowAsync(id))
        {
            // Check if it's because the flow is part of a project
            var isPartOfProject = await dbContext.ProjectFlows
                .AnyAsync(pf => pf.FlowId == id);

            if (isPartOfProject)
            {
                throw new InvalidOperationException($"Cannot delete flow {id} because it is part of one or more projects. Remove it from all projects first.");
            }

            throw new UnauthorizedAccessException($"You do not have permission to delete flow {id}");
        }

        var flow = dbContext.Flows.SingleOrDefault(x => x.Id == id);

        if (flow == null)
        {
            throw new InvalidOperationException($"Flow {id} not found");
        }

        var flowName = flow.Name;

        // ✅ Delete associated sensitive variables
        await DeleteSensitiveVariablesAsync(id);

        dbContext.Flows.Remove(flow);
        dbContext.SaveChanges();

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowDeleted, flowName, id);
    }

    public bool Exists(string name)
    {
        var exists = dbContext.Flows.Any(x => x.Name == name && x.FlowUsers.Any(x => x.UserId == userId));

        return exists;
    }

    /// <summary>
    /// Resolve flow name to flow ID for the current user (respects impersonation).
    /// </summary>
    /// <param name="flowName">Flow name to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Flow ID if found, null otherwise</returns>
    public async Task<Guid?> ResolveFlowNameToId(string flowName, CancellationToken cancellationToken = default)
    {
        // Use authorization service query to check access (personal + project-based)
        var flow = await authorizationService.GetAccessibleFlowsQuery()
            .FirstOrDefaultAsync(f => f.Name == flowName, cancellationToken);

        return flow?.Id;
    }

    public async Task<IEnumerable<RunnerFlowResult>> Execute(Guid flowId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        // Check if user can execute this flow (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanExecuteFlowAsync(flowId, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to execute flow {flowId}");
        }

        var flow = dbContext.Flows.SingleOrDefault(x => x.Id == flowId);

        if (flow == null)
        {
            throw new InvalidOperationException($"Flow {flowId} not found");
        }

        var selectedRunnerIds = runnerIds.Distinct().ToList();
        if (selectedRunnerIds.Count > 0)
            throw new UnauthorizedAccessException("Raw workflow execution is disabled on Machine Management Runners.");
        
        // Note: Flow decryption happens when Runner loads flow via API
        // The Get(Guid id) method returns flow with decrypted values
        var result = await runnerService.ExecuteFlow(flowId, selectedRunnerIds, cancellationToken);

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowExecuted, flow.Name, flowId, cancellationToken);

        return result;
    }

    public FlowSettings GetSettings(Guid id)
    {
        var flow = Get(id);

        var settings = new FlowSettings
        {
            Id = flow.Id,
            Name = flow.Name,
            Created = flow.Created,
            Updated = flow.Updated,
        };

        return settings;
    }

    public async Task UpdateSettings(Guid id, FlowSettings settings)
    {
        // Check if user can update this flow
        if (!await authorizationService.CanUpdateFlowAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update flow {id}");
        }

        var entity = dbContext.Flows.SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"Flow {id} not found");
        }

        // Deserialize the flow body, update the name, and serialize it back
        var flow = DeserializeFlow(entity.Body);
        flow.Name = settings.Name;

        // Update both the entity name and the serialized body
        entity.Name = settings.Name;
        entity.Body = JsonSerializer.Serialize(flow);
        entity.Updated = DateTime.UtcNow;

        dbContext.SaveChanges();

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowUpdated, settings.Name, id);
    }

    #region Sensitive Variables Handling

    /// <summary>
    /// Process sensitive variables: encrypt and replace with references
    /// Called during Create/Update operations
    /// </summary>
    private async Task ProcessSensitiveVariablesAsync(Flow flow)
    {
        // Process Variables dictionary
        await ProcessVariableDictionaryAsync(flow.Variables, flow.Id, SensitiveVariableScope.Input);

        // Process InputVariables dictionary
        await ProcessVariableDictionaryAsync(flow.InputVariables, flow.Id, SensitiveVariableScope.Input);

        // Process OutputVariables dictionary
        await ProcessVariableDictionaryAsync(flow.OutputVariables, flow.Id, SensitiveVariableScope.Output);
    }

    private async Task ProcessVariableDictionaryAsync(
        IDictionary<string, StepVariable> variables,
        Guid flowId,
        string scope)
    {
        foreach (var kvp in variables.ToList())
        {
            var variable = kvp.Value;

            if (variable.IsSensitive && !string.IsNullOrEmpty(variable.Value))
            {
                // Check if value is already a reference
                if (!IsSensitiveReference(variable.Value))
                {
                    try
                    {
                        // Encrypt and store in SensitiveVariables table
                        var sensitiveVar = await sensitiveVariableService.CreateOrUpdateSensitiveVariableAsync(
                            userId,
                            variable.Name,
                            variable.Value,
                            SensitiveVariableResourceType.Flow,
                            flowId,
                            scope,
                            variable.Description);

                        // Replace value with reference in the flow
                        variable.Value = $"${{sensitive:{sensitiveVar.Id}}}";

                        logger.Debug("Encrypted sensitive variable '{Name}' for flow {FlowId}", variable.Name, flowId);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to encrypt sensitive variable '{Name}' for flow {FlowId}", variable.Name, flowId);
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Decrypt sensitive variables before execution or display
    /// Called during Get operation
    /// </summary>
    private async Task DecryptSensitiveVariablesAsync(Flow flow)
    {
        await DecryptVariableDictionaryAsync(flow.Variables);
        await DecryptVariableDictionaryAsync(flow.InputVariables);
        await DecryptVariableDictionaryAsync(flow.OutputVariables);
    }

    private async Task DecryptVariableDictionaryAsync(IDictionary<string, StepVariable> variables)
    {
        foreach (var kvp in variables.ToList())
        {
            var variable = kvp.Value;

            if (variable.IsSensitive && IsSensitiveReference(variable.Value))
            {
                var decryptedValue = await GetDecryptedSensitiveValueAsync(variable.Value);
                if (decryptedValue != null)
                {
                    variable.Value = decryptedValue;
                    logger.Debug("Decrypted sensitive variable '{Name}' for execution", variable.Name);
                }
                else
                {
                    logger.Warning("Failed to decrypt sensitive variable '{Name}', using empty value", variable.Name);
                    variable.Value = string.Empty;
                }
            }
        }
    }

    private bool IsSensitiveReference(string? value)
    {
        return value?.StartsWith("${sensitive:") == true && value.EndsWith("}");
    }

    private async Task<string?> GetDecryptedSensitiveValueAsync(string reference)
    {
        try
        {
            // Extract GUID from "${sensitive:guid}"
            var guidPart = reference.Substring(12, reference.Length - 13);
            if (Guid.TryParse(guidPart, out var sensitiveVarId))
            {
                return await sensitiveVariableService.GetDecryptedValueAsync(sensitiveVarId, userId);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to decrypt sensitive reference {Reference}", reference);
        }
        return null;
    }

    /// <summary>
    /// Delete all sensitive variables associated with a flow
    /// Called during Remove operation
    /// </summary>
    private async Task DeleteSensitiveVariablesAsync(Guid flowId)
    {
        try
        {
            var sensitiveVars = await sensitiveVariableService.GetSensitiveVariablesForResourceAsync(
                SensitiveVariableResourceType.Flow,
                flowId,
                userId);

            foreach (var sensitiveVar in sensitiveVars)
            {
                await sensitiveVariableService.DeleteSensitiveVariableAsync(sensitiveVar.Id, userId);
            }

            logger.Debug("Deleted {Count} sensitive variables for flow {FlowId}", sensitiveVars.Count, flowId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to delete sensitive variables for flow {FlowId}", flowId);
            // Don't throw - allow flow deletion to proceed
        }
    }

    #endregion

    private static Flow DeserializeFlow(string jsonFlow)
    {
        var flow = JsonSerializer.Deserialize<Flow>(jsonFlow);

        return flow;
    }
}
