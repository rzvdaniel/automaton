using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for managing application settings
/// </summary>
[Authorize(Roles = "Admin,SysAdmin")]
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

    [HttpGet("app-deploy-rules")]
    public async Task<ActionResult<Models.AppDeployRulesSettings>> GetAppDeployRules()
    {
        try
        {
            return Ok(await _settingsService.GetAppDeployRulesAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving app deploy rules");
            return StatusCode(500, new { error = "Failed to retrieve app deploy rules" });
        }
    }

    [HttpPut("app-deploy-rules")]
    public async Task<IActionResult> UpdateAppDeployRules([FromBody] Automaton.Studio.Server.Models.AppDeployRulesSettings settings)
    {
        try
        {
            await _settingsService.SaveAppDeployRulesAsync(settings);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating app deploy rules");
            return BadRequest(new { error = "Failed to update app deploy rules" });
        }
    }
}
