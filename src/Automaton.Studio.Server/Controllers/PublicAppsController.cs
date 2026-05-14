using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Public API endpoints for viewing user profiles and their published apps.
/// No authentication required - these are public-facing endpoints.
/// </summary>
[Route("api/public")]
[ApiController]
[AllowAnonymous]
public class PublicAppsController : ControllerBase
{
    private readonly PublicAppsService publicAppsService;
    private readonly ILogger<PublicAppsController> logger;

    public PublicAppsController(
        PublicAppsService publicAppsService,
        ILogger<PublicAppsController> logger)
    {
        this.publicAppsService = publicAppsService;
        this.logger = logger;
    }

    /// <summary>
    /// Get public profile and apps for a user.
    /// Route: GET /api/public/{publicUsername}/apps
    /// </summary>
    [HttpGet("{publicUsername}/apps")]
    public async Task<ActionResult<PublicAppsResponse>> GetUserPublicApps(string publicUsername)
    {
        try
        {
            logger.LogInformation("[PublicAppsController] Fetching public apps for user: {PublicUsername}", publicUsername);

            // Validate username format (prevent injection)
            if (string.IsNullOrWhiteSpace(publicUsername) || publicUsername.Length > 50 || !IsValidUsername(publicUsername))
            {
                return BadRequest(new { message = "Invalid username format" });
            }

            var profile = await publicAppsService.GetPublicProfileAsync(publicUsername);

            if (profile == null)
            {
                logger.LogWarning("[PublicAppsController] User not found: {PublicUsername}", publicUsername);
                return NotFound(new { message = "User not found" });
            }

            var currentUserId = TryGetCurrentUserId();
            var apps = await publicAppsService.GetPublicAppsAsync(publicUsername, currentUserId);

            var response = new PublicAppsResponse
            {
                Profile = profile,
                Apps = apps
            };

            logger.LogInformation("[PublicAppsController] Returning {AppCount} public apps for user: {PublicUsername}",
                apps.Count, publicUsername);

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PublicAppsController] Error fetching public apps for user: {PublicUsername}", publicUsername);
            return StatusCode(500, new { message = "An error occurred while fetching apps" });
        }
    }

    /// <summary>
    /// Get public profile for a user.
    /// Route: GET /api/public/{publicUsername}/profile
    /// </summary>
    [HttpGet("{publicUsername}/profile")]
    public async Task<ActionResult<PublicUserProfile>> GetUserProfile(string publicUsername)
    {
        try
        {
            // Validate username format
            if (string.IsNullOrWhiteSpace(publicUsername) || publicUsername.Length > 50 || !IsValidUsername(publicUsername))
            {
                return BadRequest(new { message = "Invalid username format" });
            }

            var profile = await publicAppsService.GetPublicProfileAsync(publicUsername);

            if (profile == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(profile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PublicAppsController] Error fetching profile for user: {PublicUsername}", publicUsername);
            return StatusCode(500, new { message = "An error occurred while fetching profile" });
        }
    }

    /// <summary>
    /// Get details of a specific public app.
    /// Route: GET /api/public/{publicUsername}/apps/{appId}
    /// </summary>
    [HttpGet("{publicUsername}/apps/{appId:guid}")]
    public async Task<ActionResult<PublicAppDetails>> GetPublicAppDetails(string publicUsername, Guid appId)
    {
        try
        {
            // Validate username format
            if (string.IsNullOrWhiteSpace(publicUsername) || publicUsername.Length > 50 || !IsValidUsername(publicUsername))
            {
                return BadRequest(new { message = "Invalid username format" });
            }

            var currentUserId = TryGetCurrentUserId();
            var app = await publicAppsService.GetPublicAppDetailsAsync(publicUsername, appId, currentUserId);

            if (app == null)
            {
                return NotFound(new { message = "App not found or not published" });
            }

            return Ok(app);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PublicAppsController] Error fetching app details: {AppId} for user: {PublicUsername}",
                appId, publicUsername);
            return StatusCode(500, new { message = "An error occurred while fetching app details" });
        }
    }

    /// <summary>
    /// Validate public username format to prevent injection attacks
    /// Allows alphanumeric, hyphens, and underscores only (URL-safe)
    /// </summary>
    private static bool IsValidUsername(string username)
    {
        // Allow alphanumeric, underscore, and hyphen (no @ symbol needed)
        return System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_\-]+$");
    }

    private Guid? TryGetCurrentUserId()
    {
        var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdString, out var userId) ? userId : null;
    }
}
