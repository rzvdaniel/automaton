using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("/internal/runtime")]
public class InternalRuntimeController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly RuntimeCookieService runtimeCookieService;
    private readonly ILogger<InternalRuntimeController> logger;
    private readonly PublishedAppsConfig publishedAppsConfig;

    public InternalRuntimeController(
        ApplicationDbContext dbContext,
        ResourceAuthorizationService authorizationService,
        RuntimeCookieService runtimeCookieService,
        ILogger<InternalRuntimeController> logger,
        IOptions<PublishedAppsConfig> publishedAppsConfig)
    {
        this.dbContext = dbContext;
        this.authorizationService = authorizationService;
        this.runtimeCookieService = runtimeCookieService;
        this.logger = logger;
        this.publishedAppsConfig = publishedAppsConfig.Value;
    }

    [HttpGet("auth")]
    public async Task<IActionResult> AuthorizeRuntimeRequest(
        [FromHeader(Name = RuntimeCookieConstants.AppIdHeader)] string[]? appIdHeaders,
        [FromHeader(Name = RuntimeCookieConstants.ProxyAuthHeader)] string[]? proxyAuthHeaders,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorizedProxyRequest(proxyAuthHeaders?.FirstOrDefault()))
        {
            logger.LogWarning("Rejected runtime auth request without valid proxy auth header.");
            return Unauthorized();
        }

        if (!Guid.TryParse(appIdHeaders?.FirstOrDefault(), out var appId))
        {
            logger.LogDebug("Runtime auth failed: missing or invalid app id header.");
            return Unauthorized();
        }

        var cookie = Request.Cookies[RuntimeCookieConstants.GetCookieName(appId)];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            logger.LogDebug("Runtime auth failed for app {AppId}: missing cookie.", appId);
            return Unauthorized();
        }

        var validation = runtimeCookieService.ValidateRuntimeCookie(cookie, appId);
        if (!validation.IsValid)
        {
            logger.LogDebug("Runtime auth failed for app {AppId}: invalid cookie.", appId);
            return Unauthorized();
        }

        var appExists = await dbContext.WebApps
            .AnyAsync(a => a.Id == appId && a.AccessStatus == AppAccessStatuses.Active, cancellationToken);
        if (!appExists)
        {
            logger.LogDebug("Runtime auth failed for app {AppId}: app unavailable.", appId);
            return Unauthorized();
        }

        if (!await authorizationService.CanAccessAppAsync(appId, validation.UserId, cancellationToken))
        {
            logger.LogDebug("Runtime auth forbidden for app {AppId} and user {UserId}.", appId, validation.UserId);
            return Forbid();
        }

        return NoContent();
    }

    private bool IsAuthorizedProxyRequest(string? providedValue)
    {
        if (string.IsNullOrWhiteSpace(publishedAppsConfig.RuntimeProxyAuthKey))
        {
            return false;
        }

        if (providedValue is null)
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedValue);
        var expectedBytes = Encoding.UTF8.GetBytes(publishedAppsConfig.RuntimeProxyAuthKey);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
