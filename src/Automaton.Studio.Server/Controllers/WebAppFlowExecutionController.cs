using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for executing flows from published web apps.
/// Handles authentication based on app settings and executes flows as the app owner.
/// </summary>
[ApiController]
[Route("api/web-apps")]
public class WebAppFlowExecutionController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly FlowsServiceFactory flowsServiceFactory;
    private readonly RunnerService runnerService;
    private readonly ILogger<WebAppFlowExecutionController> logger;

    public WebAppFlowExecutionController(
          ApplicationDbContext dbContext,
    FlowsServiceFactory flowsServiceFactory,
          RunnerService runnerService,
          ILogger<WebAppFlowExecutionController> logger)
    {
        this.dbContext = dbContext;
        this.flowsServiceFactory = flowsServiceFactory;
        this.runnerService = runnerService;
        this.logger = logger;
    }

    /// <summary>
    /// Execute a flow on behalf of a published web app.
    /// Authenticates based on app's RequiresAuthentication setting.
    /// Always executes as the app owner (not the viewer).
    /// </summary>
    [HttpPost("{appId:guid}/flows/{flowId:guid}/execute")]
    [AllowAnonymous] // We check authentication ourselves based on app settings
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteFlow(
        Guid appId,
        Guid flowId,
        [FromBody] WebAppFlowExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Flow execution request: AppId={AppId}, FlowId={FlowId}", appId, flowId);

            // 1. Get web app and verify it's published
            var webApp = await dbContext.WebApps
             .Include(wa => wa.WebAppUsers)
                       .FirstOrDefaultAsync(wa => wa.Id == appId && wa.IsPublished, cancellationToken);

            if (webApp == null)
            {
                logger.LogWarning("Web app not found or not published: {AppId}", appId);
                return NotFound(new { message = "Web app not found or not published" });
            }

            // 2. Check authentication requirements
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;

            if (webApp.RequiresAuthentication && !isAuthenticated)
            {
                logger.LogWarning("Unauthenticated access attempt to protected app '{AppName}' ({AppId})",
             webApp.Name, appId);
                return Unauthorized(new { message = "This app requires authentication" });
            }

            if (isAuthenticated)
            {
                logger.LogInformation("Authenticated user '{User}' accessing app '{AppName}'",
                       User.Identity.Name, webApp.Name);
            }
            else
            {
                logger.LogInformation("Anonymous access to public app '{AppName}'", webApp.Name);
            }

            // 3. Get app owner
            var appOwnerId = webApp.WebAppUsers.FirstOrDefault()?.UserId;
            if (appOwnerId == null)
            {
                logger.LogError("Web app '{AppName}' ({AppId}) has no owner", webApp.Name, appId);
                return StatusCode(500, new { message = "Web app configuration error: no owner" });
            }

            logger.LogInformation("Executing flow {FlowId} as app owner {OwnerId}", flowId, appOwnerId);

            // 4. Create FlowsService scoped to app owner (KEY FIX!)
            var ownerFlowsService = flowsServiceFactory.CreateForUser(appOwnerId.Value);

            // 5. Get flow (using owner's permissions)
            var flow = ownerFlowsService.Get(flowId);
            if (flow == null)
            {
                logger.LogWarning("Flow not found or not accessible: {FlowId}", flowId);
                return NotFound(new { message = "Flow not found" });
            }

            // 6. Apply input variables from request
            if (request.Inputs != null && request.Inputs.Any())
            {
                logger.LogDebug("Applying {Count} input variables to flow {FlowId}",
                   request.Inputs.Count, flowId);

                foreach (var input in request.Inputs)
                {
                    var matchingVar = flow.InputVariables.Values
                  .FirstOrDefault(v => v.Name.Equals(input.Key, StringComparison.OrdinalIgnoreCase));

                    if (matchingVar != null)
                    {
                        matchingVar.Value = input.Value;
                        logger.LogDebug("Set input '{Key}' = {Value}", input.Key, input.Value);
                    }
                    else
                    {
                        logger.LogWarning("Input variable '{Key}' not found in flow's InputVariables", input.Key);
                    }
                }
            }

            // 7. Get available runners (owner's runners)
            var allRunners = await runnerService.List(cancellationToken);
            var onlineRunners = allRunners.Where(r => r.Status == RunnerStatus.Online).ToList();

            if (!onlineRunners.Any())
            {
                logger.LogWarning("No online runners available");
                return BadRequest(new
                {
                    success = false,
                    message = "No online runners available to execute the flow"
                });
            }

            // 8. Select runners based on request
            List<Guid> selectedRunnerIds;

            if (request.RunnerNames != null && request.RunnerNames.Any())
            {
                // User specified runner names - find matching online runners
                var requestedRunners = onlineRunners
                     .Where(r => request.RunnerNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                     .ToList();

                if (!requestedRunners.Any())
                {
                    var available = string.Join(", ", onlineRunners.Select(r => r.Name));
                    logger.LogWarning("None of specified runners are online. Requested: {Requested}, Available: {Available}",
                              string.Join(", ", request.RunnerNames), available);

                    return BadRequest(new
                    {
                        success = false,
                        message = $"None of the specified runners are online. Available: {available}",
                        availableRunners = onlineRunners.Select(r => r.Name).ToList()
                    });
                }

                selectedRunnerIds = requestedRunners.Select(r => r.Id).ToList();
                logger.LogInformation("Using specified runners: {Runners}",
               string.Join(", ", requestedRunners.Select(r => r.Name)));
            }
            else
            {
                // No runners specified - use first available online runner
                var firstRunner = onlineRunners.First();
                selectedRunnerIds = new List<Guid> { firstRunner.Id };
                logger.LogInformation("No runners specified, using first available: {Runner}", firstRunner.Name);
            }

            // 9. Execute flow as app owner (using owner's permissions and runners)
            logger.LogInformation("Executing flow {FlowId} on {RunnerCount} runner(s) on behalf of user {OwnerId}",
                       flowId, selectedRunnerIds.Count, appOwnerId);

            var results = await ownerFlowsService.Execute(flowId, selectedRunnerIds, cancellationToken);

            // 10. Process results
            var firstResult = results.FirstOrDefault();
            var success = firstResult?.Status == WorkflowStatus.Success;

            logger.LogInformation(
             "Flow execution complete: FlowId={FlowId}, Success={Success}, Status={Status}, AppId={AppId}, OwnerId={OwnerId}",
         flowId, success, firstResult?.Status, appId, appOwnerId);

            // 11. Return result
            return Ok(new WebAppFlowExecutionResponse
            {
                Success = success,
                FlowId = flowId,
                Status = firstResult?.Status.ToString() ?? "Unknown",
                RunnerCount = results.Count(),
                ExecutedAt = DateTime.UtcNow,
                Message = success ? "Flow executed successfully" : "Flow execution failed or incomplete",
                OutputVariables = firstResult?.OutputVariables ?? new Dictionary<string, object?>(),
                Logs = firstResult?.Logs
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing flow {FlowId} for app {AppId}", flowId, appId);
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while executing the flow",
                error = ex.Message
            });
        }
    }
}

/// <summary>
/// Request model for flow execution from web apps
/// </summary>
public class WebAppFlowExecutionRequest
{
    /// <summary>
    /// Input variables to pass to the flow
    /// </summary>
    public Dictionary<string, object>? Inputs { get; set; }

    /// <summary>
    /// Optional list of runner names to execute the flow on.
    /// If not specified, the first available online runner will be used.
    /// </summary>
    public List<string>? RunnerNames { get; set; }
}

/// <summary>
/// Response model for flow execution
/// </summary>
public class WebAppFlowExecutionResponse
{
    public bool Success { get; set; }
    public Guid FlowId { get; set; }
    public string Status { get; set; }
    public int RunnerCount { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string Message { get; set; }
    public Dictionary<string, object?> OutputVariables { get; set; }
    public List<string>? Logs { get; set; }
}
