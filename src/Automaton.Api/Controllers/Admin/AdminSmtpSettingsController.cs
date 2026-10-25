using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/installation/smtp")]
[Authorize(Roles = UserRole.SystemAdministrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSmtpSettingsController(SmtpSettingsService settings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SmtpSettingsSummary>> Get(CancellationToken token)
    {
        try { return Ok(await settings.GetSummaryAsync(token)); }
        catch (SmtpSettingsUnreadableException error) { return Conflict(new { message = error.Message }); }
    }

    [HttpPut]
    public async Task<IActionResult> Put(SmtpSettingsUpdate input, CancellationToken token)
    {
        try { await settings.SaveAsync(input, token); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        catch (SmtpSettingsUnreadableException error) { return Conflict(new { message = error.Message }); }
        return NoContent();
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(CancellationToken token)
    {
        try { await settings.VerifyAsync(token); }
        catch (SmtpSettingsUnreadableException error) { return Conflict(new { message = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return BadRequest(new { message = "SMTP connection or authentication failed. Check the saved settings and try again." });
        }
        return NoContent();
    }

    [HttpPost("test-email")]
    public async Task<IActionResult> SendTest(SmtpTestEmailRequest input, CancellationToken token)
    {
        try { await settings.SendTestAsync(input.Recipient, token); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        catch (SmtpSettingsUnreadableException error) { return Conflict(new { message = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return BadRequest(new { message = "The SMTP server did not accept the test message. Check the saved settings." });
        }
        return NoContent();
    }
}
