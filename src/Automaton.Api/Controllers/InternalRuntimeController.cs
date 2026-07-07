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
    public async Task<IActionResult> AuthorizeRuntimeRequest(CancellationToken cancellationToken)
    {
        if (!IsAuthorizedProxyRequest())
        {
            logger.LogWarning("Rejected runtime auth request without valid proxy auth header.");
            return Unauthorized();
        }

        if (!Request.Headers.TryGetValue(RuntimeCookieConstants.AppIdHeader, out var appIdHeader) ||
            !Guid.TryParse(appIdHeader.FirstOrDefault(), out var appId))
        {
            logger.LogDebug("Runtime auth failed: missing or invalid app id header.");
            return Unauthorized();
        }

        var cookie = Request.Cookies[RuntimeCookieConstants.CookieName];
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

    private bool IsAuthorizedProxyRequest()
    {
        if (string.IsNullOrWhiteSpace(publishedAppsConfig.RuntimeProxyAuthKey))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue(RuntimeCookieConstants.ProxyAuthHeader, out var headerValues))
        {
            return false;
        }

        var providedValue = headerValues.FirstOrDefault();
        if (providedValue is null)
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedValue);
        var expectedBytes = Encoding.UTF8.GetBytes(publishedAppsConfig.RuntimeProxyAuthKey);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
