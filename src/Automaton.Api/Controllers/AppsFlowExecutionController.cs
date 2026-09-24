using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Automaton.Api.Controllers;

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
    private readonly PrivateAppLaunchService privateAppLaunchService;

    public AppsFlowExecutionController(
        AppsService appsService,
        UserImpersonationServiceFactory<FlowsService> flowsServiceFactory,
        RunnerService runnerService,
        ILogger<AppsFlowExecutionController> logger,
        PrivateAppLaunchService privateAppLaunchService)
    {
        this.appsService = appsService;
        this.flowsServiceFactory = flowsServiceFactory;
        this.runnerService = runnerService;
        this.logger = logger;
        this.privateAppLaunchService = privateAppLaunchService;
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
    [EnableRateLimiting(RateLimitPolicyNames.PublicAppFlowExecution)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteFlow(
        string appSlug,
        string flowIdentifier,
        [FromBody] WebAppFlowExecutionRequest? request,
        [FromHeader(Name = AppExecutionContextKeys.TestRunIdHeader)] string[]? testRunIdHeaders,
        CancellationToken cancellationToken)
    {
        request ??= new WebAppFlowExecutionRequest();

        logger.LogDebug("Flow execution request for app '{AppSlug}', identifier '{FlowIdentifier}'",
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
        var flowResolution = await PublicAppFlowExecutionSupport.ResolveFlowAsync(
            ownerFlowsService,
            flowIdentifier,
            ownerId,
            logger,
            isPreview: false,
            cancellationToken);
        if (flowResolution.NotFoundMessage is not null)
        {
            return NotFound(new { message = flowResolution.NotFoundMessage });
        }
        var flowId = flowResolution.FlowId!.Value;

        logger.LogDebug("Executing flow {FlowId} for app {AppSlug} as owner {OwnerId}", flowId, appSlug, ownerId);

        // 4. Load flow and apply inputs
        var flowToExecute = flowResolution.Flow!;

        var run = await PublicAppFlowExecutionSupport.RunAsync(
            ownerFlowsService,
            runnerService,
            flowToExecute,
            flowId,
            request,
            testRunIdHeaders?.FirstOrDefault(),
            webApp.ExecutionRunnerId,
            cancellationToken);
        if (run.MissingInputs.Count > 0)
        {
            // Log once with context (flowId and appSlug) � do not leak secret values
            logger.LogWarning("Missing input variables for flow {FlowId} (appSlug: {AppSlug}): {MissingKeys}",
                flowId, appSlug, string.Join(", ", run.MissingInputs));
        }

        // 5. Get owner's runners (impersonation already set) and select
        // If there are no online runners at all then we cannot proceed
        if (!run.HasOnlineRunners)
        {
            logger.LogWarning("No online runners available for owner {OwnerId}", ownerId);
            return BadRequest(new { success = false, message = "No online runners available to execute the flow" });
        }

        if (run.RunnerSelectionError is not null)
        {
            logger.LogWarning("Runner selection failed for app {AppId}: {SelectionError}", webApp.Id, run.RunnerSelectionError);
            return BadRequest(new { success = false, message = run.RunnerSelectionError });
        }

        // 6. Execute and return
        var response = run.Response!;

        logger.LogDebug("Flow execution complete: FlowId={FlowId}, Success={Success}, AppSlug={AppSlug}, OwnerId={OwnerId}",
            flowId, response.Success, appSlug, ownerId);

        return Ok(response);
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
        [FromHeader(Name = AppExecutionContextKeys.TestRunIdHeader)] string[]? testRunIdHeaders,
        CancellationToken cancellationToken)
    {
        request ??= new WebAppFlowExecutionRequest();

        logger.LogDebug("Preview flow execution request for app '{AppId}', identifier '{FlowIdentifier}'",
            appId, flowIdentifier);

        var webApp = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);
        if (webApp == null)
            return NotFound(new { message = "Web app not found" });

        if (webApp.OwnerId == null)
        {
            logger.LogError("Web app '{AppId}' has no owner", appId);
            return StatusCode(500, new { message = "Web app configuration error: no owner" });
        }

        var ownerId = webApp.OwnerId.Value;
        var ownerFlowsService = flowsServiceFactory.CreateForUser(ownerId);

        var flowResolution = await PublicAppFlowExecutionSupport.ResolveFlowAsync(
            ownerFlowsService,
            flowIdentifier,
            ownerId,
            logger,
            isPreview: true,
            cancellationToken);
        if (flowResolution.NotFoundMessage is not null)
        {
            return NotFound(new { message = flowResolution.NotFoundMessage });
        }
        var flowId = flowResolution.FlowId!.Value;

        var flowToExecute = flowResolution.Flow!;

        var run = await PublicAppFlowExecutionSupport.RunAsync(
            ownerFlowsService,
            runnerService,
            flowToExecute,
            flowId,
            request,
            testRunIdHeaders?.FirstOrDefault(),
            webApp.ExecutionRunnerId,
            cancellationToken);
        if (run.MissingInputs.Count > 0)
        {
            logger.LogWarning("Missing preview input variables for flow {FlowId} (appId: {AppId}): {MissingKeys}",
                flowId, appId, string.Join(", ", run.MissingInputs));
        }

        if (!run.HasOnlineRunners)
            return BadRequest(new { success = false, message = "No online runners available to execute the flow" });

        if (run.RunnerSelectionError is not null)
            return BadRequest(new { success = false, message = run.RunnerSelectionError });

        return Ok(run.Response);
    }

    /// <summary>
    /// Execute a flow from a private deployed app (by app ID, requires auth).
    /// </summary>
    [HttpPost("app/{appId:guid}/flows/{flowIdentifier}/execute")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> ExecuteFlowPrivate(
        Guid appId,
        string flowIdentifier,
        [FromBody] WebAppFlowExecutionRequest? request,
        [FromHeader(Name = AppExecutionContextKeys.TestRunIdHeader)] string[]? testRunIdHeaders,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true ||
            (User.HasClaim(c => c.Type == PrivateAppAuthConstants.ScopeClaim)
             && !privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId)))
        {
            return Task.FromResult<IActionResult>(Unauthorized(new { message = "Authentication required" }));
        }

        return ExecuteFlowPreview(appId, flowIdentifier, request, testRunIdHeaders, cancellationToken);
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
