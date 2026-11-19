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
/// Installing OS updates for system administrators. apt runs on the host as root; this controller only writes a request
/// file. Apps with <c>server:read</c> can read the result of the last run.
/// </summary>
[ApiController]
[Route("api/install/os-updates")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstallOsUpdatesController(ApplicationDbContext db, IConfiguration configuration, IAuditEventService audit)
    : ServerOsUpdateControllerBase(db, configuration, audit)
{
    private readonly IConfiguration _configuration = configuration;

    [HttpGet]
    [Authorize(Policy = AppPermissionAuthenticationOptions.ServerReadPolicy)]
    public async Task<ActionResult<ServerOsUpdateStatus>> Get(CancellationToken token)
        => Ok(await ServerOsUpdate.ReadAsync(_configuration, DateTimeOffset.UtcNow, token));

    [HttpPost]
    [Authorize(Policy = "DeveloperApiAccess", Roles = UserRole.SystemAdministrator)]
    public Task<IActionResult> Post([FromBody] StartServerOsUpdateRequest? request, CancellationToken token)
    {
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var requestedBy = string.IsNullOrWhiteSpace(email) ? userId.ToString() : $"{email} {userId}";
        return InstallAsync(userId == Guid.Empty ? null : userId, requestedBy, "Installing OS updates was requested", new
        {
            Scope = request?.Scope ?? "security",
            AuthenticationType = User.Identity?.AuthenticationType,
            ApiKeyScope = User.FindFirstValue("api_key_scope"),
            ApiKeyId = User.FindFirstValue("api_key_id")
        }, request?.Scope, token);
    }
}

/// <param name="Scope">"security" (default) installs only security updates; "all" installs every pending update.</param>
public sealed record StartServerOsUpdateRequest(string? Scope);
