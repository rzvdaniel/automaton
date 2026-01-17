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
    /// Get all user settings
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserSettingsDto>> GetSettings()
    {
        var userId = GetUserId();
        var settings = await _userSettingsService.GetAllSettingsAsync(userId);
        return Ok(settings);
    }

    /// <summary>
    /// Update user settings (partial update - only provided fields are updated)
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(UserSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings([FromBody] UpdateUserSettingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetUserId();

        _logger.LogInformation(
            "Updating user settings for {UserId}: Theme={Theme}, FontSize={FontSize}",
            userId, request.Theme, request.FontSize);

        var updatedSettings = await _userSettingsService.UpdateSettingsAsync(userId, request);
        return Ok(updatedSettings);
    }

}
