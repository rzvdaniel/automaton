using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Restarting the server for system administrators. The reboot runs on the host as root; this controller only writes a
/// request file. Apps with <c>server:read</c> can read when the server last restarted.
/// </summary>
[ApiController]
[Route("api/install/restart")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstallRestartController(ApplicationDbContext db, IConfiguration configuration, IAuditEventService audit)
    : ServerRestartControllerBase(db, configuration, audit)
{
    private readonly IConfiguration _configuration = configuration;

    [HttpGet]
    [Authorize(Policy = AppPermissionAuthenticationOptions.ServerReadPolicy)]
    public async Task<ActionResult<ServerRestartStatus>> Get(CancellationToken token)
        => Ok(await ServerRestart.ReadAsync(_configuration, DateTimeOffset.UtcNow, token));

    [HttpPost]
    [Authorize(Policy = "DeveloperApiAccess", Roles = UserRole.SystemAdministrator)]
    public Task<IActionResult> Post([FromBody] StartServerRestartRequest? request, CancellationToken token)
    {
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var requestedBy = string.IsNullOrWhiteSpace(email) ? userId.ToString() : $"{email} {userId}";
        return RestartAsync(userId == Guid.Empty ? null : userId, requestedBy, "A server restart was requested", new
        {
            RestartAtUtc = request?.RestartAtUtc,
            AuthenticationType = User.Identity?.AuthenticationType,
            ApiKeyScope = User.FindFirstValue("api_key_scope"),
            ApiKeyId = User.FindFirstValue("api_key_id")
        }, request?.RestartAtUtc, token);
    }
}

public sealed record StartServerRestartRequest(DateTimeOffset? RestartAtUtc);
