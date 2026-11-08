using System.Security.Claims;
using Automaton.Api.Application.Commands;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InstallController(InstallService installService, IConfiguration configuration,
    IWebHostEnvironment environment, ApplicationDbContext db, SmtpSettingsService smtpSettings,
    IAuditEventService auditEventService) : BaseController
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

    [HttpGet("summary")]
    [Authorize(Policy = "DeveloperApiAccess", Roles = UserRole.SystemAdministrator)]
    public async Task<ActionResult<InstallationSummary>> GetSummary(CancellationToken token)
    {
        var backups = await InstallationBackups.ReadAsync(configuration, token);
        return Ok(new InstallationSummary(
            configuration["Installation:Version"],
            await db.Users.CountAsync(token),
            await db.WebApps.CountAsync(token),
            await db.Runners.CountAsync(token),
            backups.LastSuccessAt));
    }

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
    [Authorize(Policy = Automaton.Api.Authentication.AppPermissionAuthenticationOptions.BackupsReadPolicy)]
    public async Task<IActionResult> GetBackups(CancellationToken token)
        => Ok(await InstallationBackups.ReadAsync(configuration, token));

    [HttpPost("backups")]
    [Authorize(Policy = "DeveloperApiAccess", Roles = UserRole.SystemAdministrator)]
    public async Task<IActionResult> StartBackup(CancellationToken token)
    {
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var requestedBy = string.IsNullOrWhiteSpace(email) ? userId.ToString() : $"{email} {userId}";

        // Audit first and fail closed, as for server updates: a root-triggering request that cannot be recorded is not sent.
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.ServerBackupRequested,
            Action = AuditActions.Requested,
            EntityType = AuditEntityTypes.ServerInstallation,
            ActorUserId = userId == Guid.Empty ? null : userId,
            Message = "A server backup was requested",
            Metadata = new
            {
                AuthenticationType = User.Identity?.AuthenticationType,
                ApiKeyScope = User.FindFirstValue("api_key_scope"),
                ApiKeyId = User.FindFirstValue("api_key_id")
            },
            FailOnError = true
        }, token);

        return await InstallationBackups.RequestAsync(configuration, requestedBy, DateTimeOffset.UtcNow, token) switch
        {
            InstallationBackupRequestResult.Accepted => Accepted(new { requested = true }),
            InstallationBackupRequestResult.InProgress => Conflict(new { message = "A backup is already requested." }),
            InstallationBackupRequestResult.TriggerBlocked => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The backup request directory on the server cannot be used. Check /var/lib/automaton/backup/requests: it must be writable and must not contain a request.json that is not a regular file."
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "This server cannot start a backup from Studio yet. Run sudo bash /opt/automaton-server/update.sh once on the server to enable it."
            })
        };
    }

    [HttpGet("backups/{name}/archive")]
    [Authorize(Policy = Automaton.Api.Authentication.AppPermissionAuthenticationOptions.BackupsReadPolicy)]
    public async Task<IActionResult> GetBackupArchive(string name, CancellationToken token)
    {
        var status = await InstallationBackups.ReadAsync(configuration, token);
        var path = InstallationBackups.GetArchivePath(configuration, status, name);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/octet-stream", name + ".tar.age", enableRangeProcessing: true);
    }

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
