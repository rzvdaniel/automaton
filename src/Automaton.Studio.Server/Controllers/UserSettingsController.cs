using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for managing user-specific settings
/// </summary>
[ApiController]
[Route("api/user-settings")]
[Authorize]
public class UserSettingsController : BaseController
{
    private readonly UserSettingsService _userSettingsService;
    private readonly ILogger<UserSettingsController> _logger;

    public UserSettingsController(
        UserSettingsService userSettingsService,
        ILogger<UserSettingsController> logger)
    {
        _userSettingsService = userSettingsService;
        _logger = logger;
    }

    /// <summary>
    /// Get editor settings for the current user
    /// </summary>
    [HttpGet("editor")]
    [ProducesResponseType(typeof(EditorSettings), StatusCodes.Status200OK)]
    public async Task<ActionResult<EditorSettings>> GetEditorSettings()
    {
        var userId = GetUserId();
        var settings = await _userSettingsService.GetEditorSettingsAsync(userId);
        return Ok(settings);
    }

    /// <summary>
    /// Update editor settings for the current user
    /// </summary>
    [HttpPut("editor")]
    [ProducesResponseType(typeof(EditorSettings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EditorSettings>> UpdateEditorSettings([FromBody] UpdateEditorSettingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetUserId();

        _logger.LogInformation(
            "Updating editor settings for user {UserId}: FontSize={FontSize}",
            userId, request.FontSize);

        await _userSettingsService.UpdateEditorSettingsAsync(userId, request);

        // Return the updated settings
        var updatedSettings = await _userSettingsService.GetEditorSettingsAsync(userId);
        return Ok(updatedSettings);
    }

    /// <summary>
    /// Get appearance settings for the current user
    /// </summary>
    [HttpGet("appearance")]
    [ProducesResponseType(typeof(AppearanceSettings), StatusCodes.Status200OK)]
    public async Task<ActionResult<AppearanceSettings>> GetAppearanceSettings()
    {
        var userId = GetUserId();
        var settings = await _userSettingsService.GetAppearanceSettingsAsync(userId);
        return Ok(settings);
    }

    /// <summary>
    /// Update appearance settings for the current user
    /// </summary>
    [HttpPut("appearance")]
    [ProducesResponseType(typeof(AppearanceSettings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AppearanceSettings>> UpdateAppearanceSettings([FromBody] UpdateAppearanceSettingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetUserId();

        _logger.LogInformation(
            "Updating appearance settings for user {UserId}: Theme={Theme}",
            userId, request.Theme);

        await _userSettingsService.UpdateAppearanceSettingsAsync(userId, request);

        // Return the updated settings
        var updatedSettings = await _userSettingsService.GetAppearanceSettingsAsync(userId);
        return Ok(updatedSettings);
    }
}
