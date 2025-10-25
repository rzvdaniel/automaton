using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/flows")]
public class FlowExecutionController : ControllerBase
{
    private readonly FlowsService _flowsService;
    private readonly RunnerService _runnerService;
    private readonly ApiKeyService _apiKeyService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<FlowExecutionController> _logger;

    public FlowExecutionController(
        FlowsService flowsService,
        RunnerService runnerService,
        ApiKeyService apiKeyService,
        ApplicationDbContext dbContext,
        ILogger<FlowExecutionController> logger)
    {
        _flowsService = flowsService;
        _runnerService = runnerService;
        _apiKeyService = apiKeyService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Execute a flow from a published web app
    /// This endpoint is called from JavaScript in published web apps
    /// Authentication is done server-side using the app owner's WebApp API key
    /// </summary>
    [HttpPost("execute")]
    [AllowAnonymous] // Anonymous from client perspective, but authenticated server-side
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteFlow([FromBody] ExecuteFlowRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Validate request
            if (string.IsNullOrEmpty(request.FlowId))
            {
                return BadRequest(new { message = "FlowId is required" });
            }

            if (!Guid.TryParse(request.FlowId, out var flowGuid))
            {
                return BadRequest(new { message = "Invalid FlowId format" });
            }

            // Get web app slug from referer or custom header
            var webAppSlug = GetWebAppSlugFromRequest();

            if (string.IsNullOrEmpty(webAppSlug))
            {
                _logger.LogWarning("Flow execution attempt without web app context");
                return Unauthorized(new { message = "This endpoint can only be called from published web apps" });
            }

            _logger.LogInformation("Flow execution request from web app. Slug: {Slug}, FlowId: {FlowId}, Runners: {Runners}",
      webAppSlug, flowGuid, request.RunnerNames != null ? string.Join(", ", request.RunnerNames) : "auto");

            // Find the web app and its owner
            var webApp = await _dbContext.WebApps
            .Include(wa => wa.WebAppUsers)
            .FirstOrDefaultAsync(wa => wa.PublicUrl == webAppSlug && wa.IsPublished, cancellationToken);

            if (webApp == null)
            {
                _logger.LogWarning("Web app not found or not published: {Slug}", webAppSlug);
                return NotFound(new { message = "Web app not found or not published" });
            }

            // Get the app owner (first user associated with the app)
            var appOwnerId = webApp.WebAppUsers.FirstOrDefault()?.UserId;
            if (appOwnerId == null)
            {
                _logger.LogError("Web app {Slug} has no associated user", webAppSlug);
                return StatusCode(500, new { message = "Web app configuration error" });
            }

            // Get the app owner's WebApp API key
            var webAppApiKey = await _apiKeyService.GetUserWebAppApiKeyAsync(appOwnerId.Value);
            if (webAppApiKey == null)
            {
                _logger.LogWarning("No WebApp API key found for user {UserId} (app: {Slug})", appOwnerId, webAppSlug);
                return Unauthorized(new
                {
                    message = "App owner must create a WebApp API key to enable flow execution from web apps",
                    hint = "Go to Settings → API Keys and create an API key with 'WebApp' scope"
                });
            }

            _logger.LogInformation("Using app owner's WebApp API key. Owner: {OwnerId}, App: {Slug}", appOwnerId, webAppSlug);

            // Check if flow exists (using app owner's context)
            var flow = _flowsService.Get(flowGuid);
            if (flow == null)
            {
                _logger.LogWarning("Flow not found: {FlowId}", flowGuid);
                return NotFound(new { message = "Flow not found" });
            }

            // Get available runners (using app owner's context)
            var allRunners = await _runnerService.List(cancellationToken);
            var onlineRunners = allRunners.Where(r => r.Status == RunnerStatus.Online).ToList();

            if (!onlineRunners.Any())
            {
                _logger.LogWarning("No online runners available for flow execution");
                return BadRequest(new
                {
                    success = false,
                    message = "No online runners available to execute the flow"
                });
            }

            // Select runners based on request
            List<Guid> selectedRunnerIds;

            if (request.RunnerNames != null && request.RunnerNames.Any())
            {
                // User specified runner names - find matching online runners
                var requestedRunners = onlineRunners
          .Where(r => request.RunnerNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                  .ToList();

                if (!requestedRunners.Any())
                {
                    var availableRunners = string.Join(", ", onlineRunners.Select(r => r.Name));
                    _logger.LogWarning("None of the specified runners are online. Requested: {Requested}, Available: {Available}",
                 string.Join(", ", request.RunnerNames), availableRunners);

                    return BadRequest(new
                    {
                        success = false,
                        message = $"None of the specified runners are online. Available runners: {availableRunners}",
                        availableRunners = onlineRunners.Select(r => r.Name).ToList()
                    });
                }

                selectedRunnerIds = requestedRunners.Select(r => r.Id).ToList();
                _logger.LogInformation("Using specified runners: {Runners}", string.Join(", ", requestedRunners.Select(r => r.Name)));
            }
            else
            {
                // No runners specified - use first available online runner
                var firstRunner = onlineRunners.First();
                selectedRunnerIds = new List<Guid> { firstRunner.Id };
                _logger.LogInformation("No runners specified, using first available runner: {Runner}", firstRunner.Name);
            }

            _logger.LogInformation("Executing flow {FlowId} on {RunnerCount} runner(s) on behalf of user {OwnerId}",
               flowGuid, selectedRunnerIds.Count, appOwnerId);

            // Execute the flow (as app owner)
            var results = await _flowsService.Execute(flowGuid, selectedRunnerIds, cancellationToken);

            // Process results
            var firstResult = results.FirstOrDefault();
            var success = firstResult?.Status == WorkflowStatus.Success;

            _logger.LogInformation("Flow executed. FlowId: {FlowId}, Success: {Success}, Status: {Status}, WebApp: {Slug}",
              flowGuid, success, firstResult?.Status, webAppSlug);

            return Ok(new
            {
                success = success,
                flowId = flowGuid,
                status = firstResult?.Status.ToString() ?? "Unknown",
                runnerCount = results.Count(),
                executedAt = DateTime.UtcNow,
                message = success ? "Flow executed successfully" : "Flow execution failed or incomplete"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing flow from web app. FlowId: {FlowId}", request.FlowId);
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while executing the flow",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Extract web app slug from request (Referer header or custom header)
    /// </summary>
    private string? GetWebAppSlugFromRequest()
    {
        // Try custom header first (most reliable)
        if (Request.Headers.TryGetValue("X-WebApp-Slug", out var slugHeader))
        {
            return slugHeader.FirstOrDefault();
        }

        // Fallback to Referer header
        var referer = Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer))
        {
            // Extract slug from URL like https://localhost:5001/web/my-app
            var uri = new Uri(referer);
            var segments = uri.Segments;

            // Look for /web/{slug} pattern
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].TrimEnd('/').Equals("web", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[i + 1].TrimEnd('/');
                }
            }
        }

        return null;
    }
}

public class ExecuteFlowRequest
{
    public required string FlowId { get; set; }
    public Dictionary<string, object>? Inputs { get; set; }

    /// <summary>
    /// Optional list of runner names to execute the flow on.
    /// If not specified, the first available online runner will be used.
    /// </summary>
    public List<string>? RunnerNames { get; set; }
}
