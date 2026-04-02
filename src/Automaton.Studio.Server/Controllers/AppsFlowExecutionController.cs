using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Studio.Server.Constants;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for executing flows from published web apps.
/// Handles authentication based on app settings and executes flows as the app owner.
/// </summary>
[ApiController]
[Route("api/web-apps")]
public class AppsFlowExecutionController : ControllerBase
{
    private readonly AppsService appsService;
    private readonly UserImpersonationServiceFactory<FlowsService> flowsServiceFactory;
    private readonly RunnerService runnerService;
    private readonly ILogger<AppsFlowExecutionController> logger;

    public AppsFlowExecutionController(
        AppsService appsService,
        UserImpersonationServiceFactory<FlowsService> flowsServiceFactory,
        RunnerService runnerService,
        ILogger<AppsFlowExecutionController> logger)
    {
        this.appsService = appsService;
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
        request ??= new WebAppFlowExecutionRequest();

        logger.LogInformation("Flow execution request for app '{AppSlug}', identifier '{FlowIdentifier}'",
            appSlug, flowIdentifier);

        // 1. Load published app metadata and check auth
        var webApp = await appsService.GetPublishedAppInfoAsync(appSlug, cancellationToken);
        if (webApp == null)
            return NotFound(new { message = "Web app not found or not published" });

        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        if (webApp.RequiresAuthentication && !isAuthenticated)
        {
            logger.LogWarning("Unauthenticated access attempt to protected app '{AppName}' (slug: {AppSlug})",
                webApp.Name, appSlug);
            return Unauthorized(new { message = "This app requires authentication" });
        }

        if (webApp.OwnerId == null)
        {
            logger.LogError("Web app '{AppName}' (slug: {AppSlug}) has no owner", webApp.Name, appSlug);
            return StatusCode(500, new { message = "Web app configuration error: no owner" });
        }

        var ownerId = webApp.OwnerId.Value;

        // 2. Create impersonated FlowsService once and reuse
        var ownerFlowsService = flowsServiceFactory.CreateForUser(ownerId);

        // 3. Resolve flow identifier (GUID or name)
        Guid flowId;
        if (!Guid.TryParse(flowIdentifier, out flowId))
        {
            var resolved = await ownerFlowsService.ResolveFlowNameToId(flowIdentifier, cancellationToken);
            if (resolved == null)
            {
                logger.LogWarning("Flow name '{FlowName}' not found for owner {OwnerId}", flowIdentifier, ownerId);
                return NotFound(new { message = $"Flow '{flowIdentifier}' not found" });
            }
            flowId = resolved.Value;
        }

        logger.LogInformation("Executing flow {FlowId} for app {AppSlug} as owner {OwnerId}", flowId, appSlug, ownerId);

        // 4. Load flow and apply inputs
        var flowToExecute = ownerFlowsService.Get(flowId);
        if (flowToExecute == null)
        {
            logger.LogWarning("Flow not accessible: {FlowId}", flowId);
            return NotFound(new { message = "Flow not found" });
        }

        var inputs = request.Inputs != null
            ? new Dictionary<string, string>(request.Inputs, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var testRunId = ResolveTestRunId(request.TestRunId);
        if (!string.IsNullOrWhiteSpace(testRunId))
        {
            inputs[AppExecutionContextKeys.TestRunIdInput] = testRunId;
        }

        if (inputs.Any())
        {
            var missingInputs = ApplyInputVariables(flowToExecute, inputs);
            if (missingInputs.Any())
            {
                // Log once with context (flowId and appSlug) � do not leak secret values
                logger.LogWarning("Missing input variables for flow {FlowId} (appSlug: {AppSlug}): {MissingKeys}",
                    flowId, appSlug, string.Join(", ", missingInputs));
            }
        }

        // 5. Get owner's runners (impersonation already set) and select
        var allRunners = await runnerService.List(cancellationToken);
        var onlineRunners = allRunners.Where(r => r.Status == RunnerStatus.Online).ToList();

        // If there are no online runners at all then we cannot proceed
        if (!onlineRunners.Any())
        {
            logger.LogWarning("No online runners available for owner {OwnerId}", ownerId);
            return BadRequest(new { success = false, message = "No online runners available to execute the flow" });
        }

        List<Guid> selectedRunnerIds;

        try
        {
            selectedRunnerIds = SelectRunners(onlineRunners, request.RunnerNames, webApp.ExecutionRunnerId);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Runner selection failed for app {AppId}", webApp.Id);
            return BadRequest(new { success = false, message = ex.Message });
        }

        // 6. Execute and return
        var results = await ownerFlowsService.Execute(flowId, selectedRunnerIds, cancellationToken);
        var firstResult = results.FirstOrDefault();
        var success = firstResult?.Status == WorkflowStatus.Success;

        logger.LogInformation("Flow execution complete: FlowId={FlowId}, Success={Success}, AppSlug={AppSlug}, OwnerId={OwnerId}",
            flowId, success, appSlug, ownerId);

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

    /// <summary>
    /// Execute a flow in preview mode (by app ID, requires auth).
    /// </summary>
    [HttpPost("preview/{appId:guid}/flows/{flowIdentifier}/execute")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteFlowPreview(
        Guid appId,
        string flowIdentifier,
        [FromBody] WebAppFlowExecutionRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new WebAppFlowExecutionRequest();

        logger.LogInformation("Preview flow execution request for app '{AppId}', identifier '{FlowIdentifier}'",
            appId, flowIdentifier);

        var webApp = await appsService.GetAppInfoByIdAsync(appId, cancellationToken);
        if (webApp == null)
            return NotFound(new { message = "Web app not found" });

        if (webApp.OwnerId == null)
        {
            logger.LogError("Web app '{AppId}' has no owner", appId);
            return StatusCode(500, new { message = "Web app configuration error: no owner" });
        }

        var ownerId = webApp.OwnerId.Value;
        var ownerFlowsService = flowsServiceFactory.CreateForUser(ownerId);

        Guid flowId;
        if (!Guid.TryParse(flowIdentifier, out flowId))
        {
            var resolved = await ownerFlowsService.ResolveFlowNameToId(flowIdentifier, cancellationToken);
            if (resolved == null)
            {
                logger.LogWarning("Preview flow name '{FlowName}' not found for owner {OwnerId}", flowIdentifier, ownerId);
                return NotFound(new { message = $"Flow '{flowIdentifier}' not found" });
            }
            flowId = resolved.Value;
        }

        var flowToExecute = ownerFlowsService.Get(flowId);
        if (flowToExecute == null)
            return NotFound(new { message = "Flow not found" });

        var inputs = request.Inputs != null
            ? new Dictionary<string, string>(request.Inputs, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var testRunId = ResolveTestRunId(request.TestRunId);
        if (!string.IsNullOrWhiteSpace(testRunId))
            inputs[AppExecutionContextKeys.TestRunIdInput] = testRunId;

        if (inputs.Any())
        {
            var missingInputs = ApplyInputVariables(flowToExecute, inputs);
            if (missingInputs.Any())
            {
                logger.LogWarning("Missing preview input variables for flow {FlowId} (appId: {AppId}): {MissingKeys}",
                    flowId, appId, string.Join(", ", missingInputs));
            }
        }

        var allRunners = await runnerService.List(cancellationToken);
        var onlineRunners = allRunners.Where(r => r.Status == RunnerStatus.Online).ToList();
        if (!onlineRunners.Any())
            return BadRequest(new { success = false, message = "No online runners available to execute the flow" });

        List<Guid> selectedRunnerIds;
        try
        {
            selectedRunnerIds = SelectRunners(onlineRunners, request.RunnerNames, webApp.ExecutionRunnerId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }

        var results = await ownerFlowsService.Execute(flowId, selectedRunnerIds, cancellationToken);
        var firstResult = results.FirstOrDefault();
        var success = firstResult?.Status == WorkflowStatus.Success;

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

    // Helpers

    private static List<string> ApplyInputVariables(Flow flow, Dictionary<string, string> inputs)
    {
        var missing = new List<string>();

        if (inputs == null || inputs.Count == 0)
            return missing;

        foreach (var input in inputs)
        {
            var matchingVar = flow.InputVariables.Values
                .FirstOrDefault(v => v.Name.Equals(input.Key, StringComparison.OrdinalIgnoreCase));

            if (matchingVar != null)
            {
                matchingVar.Value = input.Value;
            }
            else
            {
                missing.Add(input.Key);
            }
        }

        return missing;
    }

    private static List<Guid> SelectRunners(IEnumerable<RunnerDetails> onlineRunners, List<string>? requestedNames, Guid? executionRunnerId)
    {
        if (requestedNames != null && requestedNames.Any())
        {
            var requestedSet = new HashSet<string>(requestedNames, StringComparer.OrdinalIgnoreCase);
            var matched = onlineRunners.Where(r => requestedSet.Contains(r.Name) && r.Status == RunnerStatus.Online).Select(r => r.Id).ToList();
            
            if (!matched.Any())
                throw new InvalidOperationException("None of the specified runners are online");
            
            return matched;
        }

        if (executionRunnerId != null)
        {
            var runner = onlineRunners.FirstOrDefault(r => r.Id == executionRunnerId.Value);

            if (runner == null || runner.Status != RunnerStatus.Online)
                throw new InvalidOperationException("Configured execution runner is not online");

            return new List<Guid> { executionRunnerId.Value };
        }

        throw new InvalidOperationException("No runner specified. Provide RunnerNames or set an execution runner on the app.");
    }

    private string? ResolveTestRunId(string? bodyTestRunId)
    {
        if (!string.IsNullOrWhiteSpace(bodyTestRunId))
            return bodyTestRunId.Trim();

        if (Request.Headers.TryGetValue(AppExecutionContextKeys.TestRunIdHeader, out var headerValues))
        {
            var headerValue = headerValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
                return headerValue.Trim();
        }

        return null;
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

    /// <summary>
    /// Optional test run identifier used to isolate preview/test executions.
    /// </summary>
    public string? TestRunId { get; set; }
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
