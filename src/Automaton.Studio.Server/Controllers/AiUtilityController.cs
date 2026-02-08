using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for AI utility endpoints that use the system AI service.
/// These endpoints provide AI capabilities using Automaton's API key.
/// </summary>
[Authorize]
public class AiUtilityController : BaseController
{
    private readonly ISystemAiService _systemAiService;
    private readonly SystemAiConfig _systemAiConfig;
    private readonly ILogger<AiUtilityController> _logger;

    public AiUtilityController(
        ISystemAiService systemAiService,
        IOptions<SystemAiConfig> systemAiConfig,
        ILogger<AiUtilityController> logger)
    {
        _systemAiService = systemAiService;
        _systemAiConfig = systemAiConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generate a short, catchy app name from a description.
    /// Uses the system AI service with Automaton's API key.
    /// </summary>
    [HttpPost("generate-app-name")]
    public async Task<ActionResult<GenerateAppNameResponse>> GenerateAppName(
        [FromBody] GenerateAppNameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest("Description is required.");
        }

        if (!_systemAiService.IsAvailable)
        {
            return StatusCode(503, "AI service is not available. Please try again later.");
        }

        try
        {
            var userId = GetUserId().ToString();
            var name = await _systemAiService.GenerateAppNameAsync(request.Description, userId);

            return Ok(new GenerateAppNameResponse { Name = name });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            return StatusCode(429, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate app name");
            return StatusCode(500, "Failed to generate app name. Please try again.");
        }
    }

    /// <summary>
    /// Generate a concise session name from a chat prompt.
    /// Uses the system AI service with Automaton's API key.
    /// </summary>
    [HttpPost("generate-session-name")]
    public async Task<ActionResult<GenerateSessionNameResponse>> GenerateSessionName(
        [FromBody] GenerateSessionNameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt is required.");
        }

        if (!_systemAiService.IsAvailable)
        {
            return StatusCode(503, "AI service is not available. Please try again later.");
        }

        try
        {
            var userId = GetUserId().ToString();
            var name = await _systemAiService.GenerateSessionNameAsync(request.Prompt, userId);

            return Ok(new GenerateSessionNameResponse { Name = name });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            return StatusCode(429, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate session name");
            return StatusCode(500, "Failed to generate session name. Please try again.");
        }
    }

    /// <summary>
    /// Check if the system AI service is available.
    /// Returns provider and model info for UI display.
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public ActionResult<AiServiceStatusResponse> GetStatus()
    {
        return Ok(new AiServiceStatusResponse
        {
            IsAvailable = _systemAiService.IsAvailable,
            Provider = _systemAiConfig.DefaultProvider,
            Model = _systemAiConfig.DefaultModel
        });
    }
}

/// <summary>
/// Request to generate an app name from a description.
/// </summary>
public class GenerateAppNameRequest
{
    /// <summary>
    /// The user's description of the app they want to build.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Response containing the generated app name.
/// </summary>
public class GenerateAppNameResponse
{
    /// <summary>
    /// The AI-generated app name (2-4 words).
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Request to generate a session name from a chat prompt.
/// </summary>
public class GenerateSessionNameRequest
{
    /// <summary>
    /// The first message/prompt in the chat session.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;
}

/// <summary>
/// Response containing the generated session name.
/// </summary>
public class GenerateSessionNameResponse
{
    /// <summary>
    /// The AI-generated session name (3-5 words).
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Response for AI service status check.
/// </summary>
public class AiServiceStatusResponse
{
    /// <summary>
    /// Whether the system AI service is available and configured.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// The provider name for the system AI (e.g., "OpenAI").
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// The model used by the system AI (e.g., "gpt-4o-mini").
    /// </summary>
    public string? Model { get; set; }
}
