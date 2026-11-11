using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps")]
public sealed class AppsRuntimeController(
    AppsService appsService,
    UserContextService userContextService,
    RuntimeCookieService runtimeCookieService,
    AppFramingProbe framingProbe,
    ResourceAuthorizationService authorizationService,
    IOptions<PublishedAppsConfig> publishedAppsOptions) : ControllerBase
{
    private readonly PublishedAppsConfig publishedAppsConfig = publishedAppsOptions.Value;

    [HttpPost("{id}/runtime-launch")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> CreateRuntimeLaunch(Guid id, CancellationToken cancellationToken)
    {
        if (await DenyAsync(id, cancellationToken) is { } denied) return denied;

        AppendRuntimeCookie(id);
        // runtimeUrl lets Studio navigate to the app directly: a plain page navigation to launch-runtime carries no bearer token.
        return Ok(new { runtimeToken = string.Empty, runtimeUrl = BuildRestrictedRuntimeUrl(id) });
    }

    /// <summary>Whether the app lets Studio show it in a frame; Studio opens a new tab when it does not.</summary>
    [HttpGet("{id}/embeddable")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetEmbeddable(Guid id, CancellationToken cancellationToken)
    {
        if (await DenyAsync(id, cancellationToken) is { } denied) return denied;

        var studioOrigin = Request.Headers.Origin.FirstOrDefault() ?? $"{Request.Scheme}://{Request.Host}";
        return Ok(new { canBeFramed = await framingProbe.CanBeFramedAsync(id, studioOrigin, cancellationToken) });
    }

    [HttpGet("{id}/launch-runtime")]
    public async Task<IActionResult> LaunchRuntime(Guid id, CancellationToken cancellationToken)
    {
        if (await DenyAsync(id, cancellationToken) is { } denied) return denied;

        AppendRuntimeCookie(id);
        return Redirect(BuildRestrictedRuntimeUrl(id));
    }

    /// <summary>Null when the signed-in user may open the app; 403 when the app exists but is not theirs.</summary>
    private async Task<IActionResult?> DenyAsync(Guid appId, CancellationToken cancellationToken)
    {
        if (await CanUseAppAsync(appId, cancellationToken)) return null;
        return await authorizationService.AppExistsAsync(appId, cancellationToken)
            ? StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have access to this app." })
            : NotFound(new { message = "App not found" });
    }

    private async Task<bool> CanUseAppAsync(Guid appId, CancellationToken cancellationToken)
    {
        var userId = userContextService.GetUserId();
        return await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken) is not null ||
               await authorizationService.CanUseAppRuntimeAsync(appId, userId, cancellationToken);
    }

    private void AppendRuntimeCookie(Guid appId)
    {
        var userId = userContextService.GetUserId();
        var expiresAtUtc = RuntimeCookieService.GetDefaultExpiry();
        var runtimeCookie = runtimeCookieService.CreateRuntimeCookie(appId, userId, expiresAtUtc);
        Response.Cookies.Append(
            RuntimeCookieConstants.GetCookieName(appId),
            runtimeCookie,
            runtimeCookieService.BuildRuntimeCookieOptions(appId, Request, expiresAtUtc));
    }

    private string BuildRestrictedRuntimeUrl(Guid appId)
    {
        var configuredBaseUrl = publishedAppsConfig.PublicBaseUrl?.Trim().TrimEnd('/');
        var baseUri = Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredUri)
            ? configuredUri
            : new Uri($"{Request.Scheme}://{Request.Host}");
        var rootDomain = baseUri.Host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase)
            ? baseUri.Host["apps.".Length..]
            : baseUri.Host;
        var builder = new UriBuilder(baseUri)
        {
            Host = $"app-{appId:N}.apps.{rootDomain}",
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString();
    }
}
