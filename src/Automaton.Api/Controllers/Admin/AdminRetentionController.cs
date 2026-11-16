using Automaton.Api.Data;
using Automaton.Api.Retention;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

/// <summary>
/// Server retention for Action runs, logs and audit events, and per-app overrides. The nightly Worker job applies it.
/// Keeping runs or logs longer than 90 days needs <c>approveLongRetention=true</c>.
/// </summary>
[ApiController]
[Route("api/admin/retention")]
[Authorize(Policy = "Administrators")]
public sealed class AdminRetentionController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RetentionDefaults>> GetDefaults(CancellationToken token)
        => Ok(await RetentionSettings.GetDefaultsAsync(db, token));

    [HttpPut]
    public async Task<ActionResult<RetentionDefaults>> SetDefaults(
        [FromBody] RetentionDefaults request, [FromQuery] bool approveLongRetention, CancellationToken token)
    {
        try { await RetentionSettings.SaveDefaultsAsync(db, request, approveLongRetention, token); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        return Ok(request);
    }

    [HttpGet("apps/{appId:guid}")]
    public async Task<ActionResult<RetentionAppOverride>> GetApp(Guid appId, CancellationToken token)
        => await RetentionSettings.GetAppOverrideAsync(db, appId, token) is { } value ? Ok(value) : NotFound();

    [HttpPut("apps/{appId:guid}")]
    public async Task<ActionResult<RetentionAppOverride>> SetApp(
        Guid appId, [FromBody] RetentionAppOverride request, [FromQuery] bool approveLongRetention, CancellationToken token)
    {
        if (!await db.WebApps.AnyAsync(x => x.Id == appId, token)) return NotFound();
        try { await RetentionSettings.SaveAppOverrideAsync(db, appId, request, approveLongRetention, token); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        return Ok(request);
    }

    [HttpDelete("apps/{appId:guid}")]
    public async Task<IActionResult> ClearApp(Guid appId, CancellationToken token)
    {
        await RetentionSettings.SaveAppOverrideAsync(db, appId, null, approved: false, token);
        return NoContent();
    }
}
