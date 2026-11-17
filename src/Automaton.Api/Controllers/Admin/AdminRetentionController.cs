using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Retention;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
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
public sealed class AdminRetentionController(ApplicationDbContext db, IAuditEventService audit, UserContextService userContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RetentionDefaults>> GetDefaults(CancellationToken token)
        => Ok(await RetentionSettings.GetDefaultsAsync(db, token));

    [HttpPut]
    public async Task<ActionResult<RetentionDefaults>> SetDefaults(
        [FromBody] RetentionDefaults request, [FromQuery] bool approveLongRetention, CancellationToken token)
    {
        try
        {
            await RetentionSettings.SaveDefaultsAsync(db, request, approveLongRetention, token);
            await AuditAsync(null, request, approveLongRetention, token);
        }
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
        try
        {
            await RetentionSettings.SaveAppOverrideAsync(db, appId, request, approveLongRetention, token);
            await AuditAsync(appId, request, approveLongRetention, token);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        return Ok(request);
    }

    [HttpDelete("apps/{appId:guid}")]
    public async Task<IActionResult> ClearApp(Guid appId, CancellationToken token)
    {
        await RetentionSettings.SaveAppOverrideAsync(db, appId, null, approved: false, token);
        await AuditAsync(appId, null, false, token);
        return NoContent();
    }

    private Task AuditAsync(Guid? appId, object? settings, bool approved, CancellationToken token)
        => audit.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.RetentionChanged,
            Action = AuditActions.Changed,
            EntityType = AuditEntityTypes.RetentionSettings,
            EntityId = appId,
            ActorUserId = userContext.GetUserId(),
            Message = appId is null ? "Server retention settings were changed" : "App retention override was changed",
            Metadata = new { appId, settings, approvedLongRetention = approved }
        }, token);
}
