using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for managing application settings
/// </summary>
public class SettingsController : BaseController
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        SettingsService settingsService,
        ILogger<SettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Get Python engine settings for desktop environment
    /// </summary>
    [HttpGet("python")]
    public async Task<ActionResult<object>> GetPythonSettings()
    {
        try
        {
            var settings = await _settingsService.GetPythonSettingsAsync();
            
            if (settings == null)
            {
                return Ok(new { }); // Return empty object if no settings
            }

            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Python settings");
            return StatusCode(500, new { error = "Failed to retrieve Python settings" });
        }
    }

    /// <summary>
    /// Update Python engine settings for desktop environment
    /// </summary>
    [HttpPut("python")]
    public async Task<IActionResult> UpdatePythonSettings([FromBody] JsonElement settings)
    {
        try
        {
            var settingsJson = settings.GetRawText();
            await _settingsService.SavePythonSettingsAsync(settingsJson);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Python settings");
            return BadRequest(new { error = "Failed to update Python settings" });
        }
    }
}
