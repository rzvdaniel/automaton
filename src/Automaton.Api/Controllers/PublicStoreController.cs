using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Public status of the app store, readable even when the store is disabled.
/// </summary>
[ApiController]
[Route("api/public/store")]
[AllowAnonymous]
public class PublicStoreController(SettingsService settingsService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<PublicStoreStatusDto>> GetStatus()
    {
        var settings = await settingsService.GetStoreSettingsAsync();
        return Ok(new PublicStoreStatusDto { Enabled = settings.IsEnabled });
    }
}
