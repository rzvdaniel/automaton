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
    /// Execute a flow on behalf of a published web app using human-friendly identifiers.
    /// Supports both app slugs and flow names/GUIDs for maximum flexibility.
    /// Authenticates based on app's RequiresAuthentication setting.
    /// Always executes as the app owner (not the viewer).
    /// </summary>
    /// <param name="appSlug">The app's public URL slug (e.g., 'nytimes-headlines')</param>
    /// <param name="flowIdentifier">Flow name (e.g., 'Get-Articles') or GUID</param>
    /// <param name="request">Execution request with inputs and optional runner names</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("{appSlug}/flows/{flowIdentifier}/execute")]
    [AllowAnonymous] // We check authentication ourselves based on app settings
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteFlow(
        string appSlug,
        string flowIdentifier,
        [FromBody] WebAppFlowExecutionRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Flow execution request: AppSlug={AppSlug}, FlowIdentifier={FlowIdentifier}",
                appSlug, flowIdentifier);

            // Handle null or empty request body
            request ??= new WebAppFlowExecutionRequest();

            logger.LogDebug("Request inputs count: {Count}, RunnerNames: {Runners}",
                request.Inputs?.Count ?? 0,
                request.RunnerNames != null ? string.Join(", ", request.RunnerNames) : "none");

            // 1. Get web app by slug and verify it's published
            var webApp = await dbContext.WebApps
                 .Include(wa => wa.WebAppUsers)
                .FirstOrDefaultAsync(wa => wa.PublicUrl == appSlug && wa.IsPublished, cancellationToken);

            if (webApp == null)
            {
                logger.LogWarning("Web app not found or not published: {AppSlug}", appSlug);
                return NotFound(new { message = "Web app not found or not published" });
            }

            logger.LogInformation("Found web app: {AppName} (ID: {AppId})", webApp.Name, webApp.Id);

            // 2. Check authentication requirements
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;

            if (webApp.RequiresAuthentication && !isAuthenticated)
            {
                logger.LogWarning("Unauthenticated access attempt to protected app '{AppName}' (slug: {AppSlug})",
                    webApp.Name, appSlug);
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
                logger.LogError("Web app '{AppName}' (slug: {AppSlug}) has no owner", webApp.Name, appSlug);
                return StatusCode(500, new { message = "Web app configuration error: no owner" });
            }

            // 4. Resolve flow identifier (can be name or GUID)
            Guid flowId;
            if (Guid.TryParse(flowIdentifier, out var parsedGuid))
            {
                // It's a GUID - use directly
                flowId = parsedGuid;
                logger.LogDebug("Flow identifier is GUID: {FlowId}", flowId);
            }
            else
            {
                // It's a flow name - look up by owner and name
                logger.LogDebug("Resolving flow name '{FlowName}' for owner {OwnerId}", flowIdentifier, appOwnerId);

                var flow = await dbContext.Flows
                    .Include(f => f.FlowUsers)
                    .FirstOrDefaultAsync(f =>
                        f.Name == flowIdentifier &&
                        f.FlowUsers.Any(u => u.UserId == appOwnerId),
                        cancellationToken);

                if (flow == null)
                {
                    logger.LogWarning("Flow '{FlowName}' not found for app owner {OwnerId}",
                        flowIdentifier, appOwnerId);
                    return NotFound(new { message = $"Flow '{flowIdentifier}' not found" });
                }

                flowId = flow.Id;
                logger.LogInformation("Resolved flow name '{FlowName}' to ID: {FlowId}", flowIdentifier, flowId);
            }

            logger.LogInformation("Executing flow {FlowId} as app owner {OwnerId}", flowId, appOwnerId);

            // 5. Create FlowsService scoped to app owner (impersonation for this request)
            // This sets the impersonation context for the rest of the request
            var ownerFlowsService = flowsServiceFactory.CreateForUser(appOwnerId.Value);

            // 6. Get flow (using owner's permissions)
            var flowToExecute = ownerFlowsService.Get(flowId);
            if (flowToExecute == null)
            {
                logger.LogWarning("Flow not found or not accessible: {FlowId}", flowId);
                return NotFound(new { message = "Flow not found" });
            }

            // 7. Apply input variables from request
            if (request.Inputs != null && request.Inputs.Any())
            {
                logger.LogDebug("Applying {Count} input variables to flow {FlowId}",
                        request.Inputs.Count, flowId);

                foreach (var input in request.Inputs)
                {
                    var matchingVar = flowToExecute.InputVariables.Values
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

            // 8. Get available runners (owner's runners) - AFTER impersonation is set
            // This ensures we get the app owner's runners, not the current user's runners
            var allRunners = await runnerService.List(cancellationToken);
            var onlineRunners = allRunners.Where(r => r.Status == RunnerStatus.Online).ToList();

            if (!onlineRunners.Any())
            {
                logger.LogWarning("No online runners available for user {OwnerId}", appOwnerId);
                return BadRequest(new
                {
                    success = false,
                    message = "No online runners available to execute the flow"
                });
            }

            // 9. Select runners based on request
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

            // 10. Execute flow as app owner (using owner's permissions and runners)
            logger.LogInformation("Executing flow {FlowId} on {RunnerCount} runner(s) on behalf of user {OwnerId}",
                flowId, selectedRunnerIds.Count, appOwnerId);

            var results = await ownerFlowsService.Execute(flowId, selectedRunnerIds, cancellationToken);

            // 11. Process results
            var firstResult = results.FirstOrDefault();
            var success = firstResult?.Status == WorkflowStatus.Success;

            logger.LogInformation(
               "Flow execution complete: FlowId={FlowId}, Success={Success}, Status={Status}, AppSlug={AppSlug}, OwnerId={OwnerId}",
                flowId, success, firstResult?.Status, appSlug, appOwnerId);

            // 12. Return result
            return Ok(new WebAppFlowExecutionResponse
            {
                Success = success,
                FlowId = flowId,
                Status = firstResult?.Status.ToString() ?? "Unknown",
                RunnerCount = results.Count(),
                ExecutedAt = DateTime.UtcNow,
                Message = success ? "Flow executed successfully" : "Flow execution failed or incomplete",
                OutputVariables = firstResult?.OutputVariables ?? [],
                Logs = firstResult?.Logs
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing flow {FlowIdentifier} for app {AppSlug}", flowIdentifier, appSlug);

            // Return detailed error for debugging
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while executing the flow",
                error = ex.Message,
                stackTrace = ex.StackTrace,
                innerError = ex.InnerException?.Message
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
    public Dictionary<string, string>? Inputs { get; set; }

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
    public Dictionary<string, string?> OutputVariables { get; set; }
    public List<string>? Logs { get; set; }
}
