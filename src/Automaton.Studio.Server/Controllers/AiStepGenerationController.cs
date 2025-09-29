using Automaton.Studio.Server.Services.AiCodeGeneration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AiStepGenerationController : ControllerBase
{
    private readonly AiStepGenerationService aiStepGenerationService;
    private readonly ILogger<AiStepGenerationController> logger;

    public AiStepGenerationController(
        AiStepGenerationService aiStepGenerationService,
        ILogger<AiStepGenerationController> logger)
    {
        this.aiStepGenerationService = aiStepGenerationService;
        this.logger = logger;
    }

    [HttpPost("generate-step-code")]
    public async Task<ActionResult<CodeGenerationResponse>> GenerateStepCode([FromBody] StepGenerationRequest request)
    {
        var userId = GetUserId();
        var result = await aiStepGenerationService.GenerateCodeAsync(userId, request);

        if (result.IsSuccess)
        {
            return Ok(new CodeGenerationResponse 
            { 
                GeneratedCode = result.GeneratedCode,
                IsSuccess = true 
            });
        }
        else
        {
            return BadRequest(new CodeGenerationResponse 
            { 
                ErrorMessage = result.ErrorMessage,
                IsSuccess = false 
            });
        }
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user ID");
        }
        return userId;
    }
}

// DTOs
public class CodeGenerationResponse
{
    public bool IsSuccess { get; set; }
    public string GeneratedCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}