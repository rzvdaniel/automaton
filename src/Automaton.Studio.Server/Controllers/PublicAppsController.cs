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
    /// Route: GET /api/public/{username}/apps
    /// </summary>
    [HttpGet("{username}/apps")]
    public async Task<ActionResult<PublicAppsResponse>> GetUserPublicApps(string username)
    {
        try
        {
            logger.LogInformation("[PublicAppsController] Fetching public apps for user: {Username}", username);

            // Validate username format (prevent injection)
            if (string.IsNullOrWhiteSpace(username) || username.Length > 50 || !IsValidUsername(username))
            {
                return BadRequest(new { message = "Invalid username format" });
            }

            var profile = await publicAppsService.GetPublicProfileAsync(username);

            if (profile == null)
            {
                logger.LogWarning("[PublicAppsController] User not found: {Username}", username);
                return NotFound(new { message = "User not found" });
            }

            var apps = await publicAppsService.GetPublicAppsAsync(username);

            var response = new PublicAppsResponse
            {
                Profile = profile,
                Apps = apps
            };

            logger.LogInformation("[PublicAppsController] Returning {AppCount} public apps for user: {Username}",
                apps.Count, username);

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PublicAppsController] Error fetching public apps for user: {Username}", username);
            return StatusCode(500, new { message = "An error occurred while fetching apps" });
        }
    }

    /// <summary>
    /// Get public profile for a user.
    /// Route: GET /api/public/{username}/profile
    /// </summary>
    [HttpGet("{username}/profile")]
    public async Task<ActionResult<PublicUserProfile>> GetUserProfile(string username)
    {
        try
        {
            // Validate username format
            if (string.IsNullOrWhiteSpace(username) || username.Length > 50 || !IsValidUsername(username))
            {
                return BadRequest(new { message = "Invalid username format" });
            }

            var profile = await publicAppsService.GetPublicProfileAsync(username);

            if (profile == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(profile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PublicAppsController] Error fetching profile for user: {Username}", username);
            return StatusCode(500, new { message = "An error occurred while fetching profile" });
        }
    }

    /// <summary>
    /// Get details of a specific public app.
    /// Route: GET /api/public/{username}/apps/{appId}
    /// </summary>
    [HttpGet("{username}/apps/{appId:guid}")]
    public async Task<ActionResult<PublicAppDetails>> GetPublicAppDetails(string username, Guid appId)
    {
        try
        {
            // Validate username format
            if (string.IsNullOrWhiteSpace(username) || username.Length > 50 || !IsValidUsername(username))
            {
                return BadRequest(new { message = "Invalid username format" });
            }

            var app = await publicAppsService.GetPublicAppDetailsAsync(username, appId);

            if (app == null)
            {
                return NotFound(new { message = "App not found or not published" });
            }

            return Ok(app);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PublicAppsController] Error fetching app details: {AppId} for user: {Username}",
                appId, username);
            return StatusCode(500, new { message = "An error occurred while fetching app details" });
        }
    }

    /// <summary>
    /// Validate username format to prevent injection attacks
    /// </summary>
    private static bool IsValidUsername(string username)
    {
        // Allow alphanumeric, underscore, hyphen, and period
        return System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_\-\.]+$");
    }
}
