using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GitHubController : ControllerBase
{
    private readonly GitHubTokenService tokenService;
    private readonly GitHubService githubService;
    private readonly UserContextService userContextService;
    private readonly Serilog.ILogger logger;

    public GitHubController(
        GitHubTokenService tokenService,
        GitHubService githubService,
        UserContextService userContextService)
    {
        this.tokenService = tokenService;
        this.githubService = githubService;
        this.userContextService = userContextService;
        this.logger = Serilog.Log.ForContext<GitHubController>();
    }

    /// <summary>
    /// Set GitHub Personal Access Token for current user
    /// </summary>
    [HttpPost("token")]
    public async Task<IActionResult> SetToken([FromBody] SetGitHubTokenRequest request)
    {
        try
        {
            await tokenService.SetTokenForUserAsync(
                userContextService.GetUserId(),
                request.Token,
                request.Username);

            return Ok(new { success = true, message = "GitHub token saved successfully" });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to set GitHub token");
            return BadRequest(new { success = false, message = "Failed to save GitHub token" });
        }
    }

    /// <summary>
    /// Check if user has a GitHub token configured
    /// </summary>
    [HttpGet("token/status")]
    public async Task<IActionResult> GetTokenStatus()
    {
        try
        {
            var hasToken = await tokenService.HasTokenAsync(userContextService.GetUserId());
            var username = hasToken ? await tokenService.GetUsernameAsync(userContextService.GetUserId()) : null;

            return Ok(new
            {
                hasToken,
                username
            });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to get token status");
            return StatusCode(500, new { success = false, message = "Failed to check token status" });
        }
    }

    /// <summary>
    /// Validate the current user's GitHub token
    /// </summary>
    [HttpPost("token/validate")]
    public async Task<IActionResult> ValidateToken()
    {
        try
        {
            var isValid = await tokenService.ValidateTokenAsync(userContextService.GetUserId());
            return Ok(new { isValid });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to validate token");
            return StatusCode(500, new { success = false, message = "Failed to validate token" });
        }
    }

    /// <summary>
    /// Remove GitHub token for current user
    /// </summary>
    [HttpDelete("token")]
    public async Task<IActionResult> RemoveToken()
    {
        try
        {
            await tokenService.RemoveTokenAsync(userContextService.GetUserId());
            return Ok(new { success = true, message = "GitHub token removed successfully" });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to remove GitHub token");
            return BadRequest(new { success = false, message = "Failed to remove GitHub token" });
        }
    }

    /// <summary>
    /// Create a new GitHub repository for an app
    /// </summary>
    [HttpPost("repositories")]
    public async Task<IActionResult> CreateRepository([FromBody] CreateRepositoryRequest request)
    {
        try
        {
            var repositoryUrl = await githubService.CreateRepositoryAsync(
                request.Name,
                request.Description ?? "",
                request.IsPrivate ?? true);

            return Ok(new
            {
                success = true,
                repositoryUrl,
                message = "Repository created successfully"
            });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to create GitHub repository");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}

public class SetGitHubTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public string? Username { get; set; }
}

public class CreateRepositoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsPrivate { get; set; }
}
