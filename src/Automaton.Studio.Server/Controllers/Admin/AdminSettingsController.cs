using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

/// <summary>
/// Admin controller for global application settings.
/// </summary>
[ApiController]
[Route("api/admin/settings")]
[Authorize(Policy = "Administrators")]
public class AdminSettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<AdminSettingsController> _logger;

    public AdminSettingsController(
        SettingsService settingsService,
        ILogger<AdminSettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpGet("app-deploy-rules")]
    public async Task<ActionResult<AppDeployRulesSettings>> GetAppDeployRules()
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
    public async Task<IActionResult> UpdateAppDeployRules([FromBody] AppDeployRulesSettings settings)
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