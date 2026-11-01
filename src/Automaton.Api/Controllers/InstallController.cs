using Automaton.Api.Application.Commands;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InstallController(InstallService installService, IConfiguration configuration,
    IWebHostEnvironment environment, ApplicationDbContext db, SmtpSettingsService smtpSettings) : BaseController
{
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetInstallationStatus()
        => Ok(new
        {
            InstallationComplete = await installService.InstallationComplete(),
            GitHubEnabled = !string.IsNullOrWhiteSpace(configuration["GitHub:ClientId"])
                && !string.IsNullOrWhiteSpace(configuration["GitHub:ClientSecret"]),
            UserSignUp = configuration.GetValue<bool>("AppConfig:UserSignUp"),
            SetupTokenRequired = !environment.IsDevelopment() || !string.IsNullOrWhiteSpace(configuration["Bootstrap:Token"])
        });

    [HttpPost("checks")]
    [AllowAnonymous]
    public async Task<IActionResult> Check(CancellationToken token)
    {
        if (await installService.InstallationComplete())
        {
            if (!User.IsInRole(UserRole.SystemAdministrator)) return Forbid();
        }
        else if (!HasSetupAccess()) return StatusCode(403, new { message = "Enter the setup token from your server." });
        return Ok(await InstallationReadiness.CheckAsync(db, configuration, smtpSettings, token));
    }

    [HttpGet("backups")]
    [Authorize(Policy = "AppPermission:backups:read")]
    public async Task<IActionResult> GetBackups(CancellationToken token)
        => Ok(await InstallationBackups.ReadAsync(configuration, token));

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Post(InstallCommand command, CancellationToken cancellationToken)
    {
        if (await installService.InstallationComplete()) return Conflict(new { message = "Installation has already been completed." });
        if (!HasSetupAccess()) return StatusCode(403, new { message = "A valid server setup token is required." });
        try { await installService.Install(command, cancellationToken); }
        catch (InstallationSetupException error) { return BadRequest(new { message = error.Message }); }
        return Ok();
    }

    private bool HasSetupAccess()
        => InstallationSetupAccess.IsAllowed(configuration, environment,
            Request.Headers[InstallationSetupAccess.HeaderName].FirstOrDefault());
}
