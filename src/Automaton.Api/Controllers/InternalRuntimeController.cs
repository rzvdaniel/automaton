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
    private readonly ConfigurationService configurationService;

    public InternalRuntimeController(
        ApplicationDbContext dbContext,
        ResourceAuthorizationService authorizationService,
        RuntimeCookieService runtimeCookieService,
        ILogger<InternalRuntimeController> logger,
        IOptions<PublishedAppsConfig> publishedAppsConfig,
        ConfigurationService configurationService)
    {
        this.configurationService = configurationService;
        this.dbContext = dbContext;
        this.authorizationService = authorizationService;
        this.runtimeCookieService = runtimeCookieService;
        this.logger = logger;
        this.publishedAppsConfig = publishedAppsConfig.Value;
    }

    [HttpGet("auth")]
    public async Task<IActionResult> AuthorizeRuntimeRequest(
        [FromHeader(Name = RuntimeCookieConstants.AppIdHeader)] string?[]? appIdHeaders,
        [FromHeader(Name = RuntimeCookieConstants.ProxyAuthHeader)] string?[]? proxyAuthHeaders,
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
            return SignInRequired(appId);
        }

        var validation = runtimeCookieService.ValidateRuntimeCookie(cookie, appId);
        if (!validation.IsValid)
        {
            logger.LogDebug("Runtime auth failed for app {AppId}: invalid cookie.", appId);
            return SignInRequired(appId);
        }

        var appExists = await dbContext.WebApps
            .AnyAsync(a => a.Id == appId && a.AccessStatus == AppAccessStatuses.Active
                && a.DeletionClaimId == null, cancellationToken);
        if (!appExists)
        {
            logger.LogDebug("Runtime auth failed for app {AppId}: app unavailable.", appId);
            return Unauthorized();
        }

        if (!await authorizationService.CanUseAppRuntimeAsync(appId, validation.UserId, cancellationToken))
        {
            logger.LogDebug("Runtime auth forbidden for app {AppId} and user {UserId}.", appId, validation.UserId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return NoContent();
    }

    /// <summary>A browser opening a page goes to Studio to sign in and come back; anything else gets 401.</summary>
    private IActionResult SignInRequired(Guid appId)
    {
        var method = Request.Headers["X-Forwarded-Method"].ToString();
        var accept = Request.Headers.Accept.ToString();
        var uri = Request.Headers["X-Forwarded-Uri"].ToString();
        // Traefik's own hop to the API strips X-Forwarded-Method/Uri on installations where the API is reached through
        // the proxy again, so an absent method counts as GET and an absent URI sends the visitor to the app's start page.
        var isPageLoad = (method.Length == 0 || string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        if (!isPageLoad) return Unauthorized();

        var returnTo = uri.StartsWith('/') && !uri.StartsWith("//", StringComparison.Ordinal)
            && !uri.Any(character => character == '\\' || char.IsControl(character)) ? uri : "/";
        var studio = configurationService.WebsiteUrl.TrimEnd('/');
        return Redirect($"{studio}/apps/{appId}/launch?to={Uri.EscapeDataString(returnTo)}");
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
