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
    ResourceAuthorizationService authorizationService,
    IOptions<PublishedAppsConfig> publishedAppsOptions) : ControllerBase
{
    private readonly PublishedAppsConfig publishedAppsConfig = publishedAppsOptions.Value;

    [HttpPost("{id}/runtime-launch")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> CreateRuntimeLaunch(Guid id, CancellationToken cancellationToken)
    {
        if (!await CanUseAppAsync(id, cancellationToken))
        {
            return NotFound(new { message = "App not found" });
        }

        AppendRuntimeCookie(id);
        return Ok(new { runtimeToken = string.Empty });
    }

    [HttpGet("{id}/launch-runtime")]
    public async Task<IActionResult> LaunchRuntime(Guid id, CancellationToken cancellationToken)
    {
        if (!await CanUseAppAsync(id, cancellationToken))
        {
            return NotFound(new { message = "App not found" });
        }

        AppendRuntimeCookie(id);
        return Redirect(BuildRestrictedRuntimeUrl(id));
    }

    private async Task<bool> CanUseAppAsync(Guid appId, CancellationToken cancellationToken)
    {
        var userId = userContextService.GetUserId();
        return await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken) is not null ||
               await authorizationService.CanExecuteAssignedOrganizationAppAsync(appId, userId, cancellationToken);
    }

    private void AppendRuntimeCookie(Guid appId)
    {
        var userId = userContextService.GetUserId();
        var expiresAtUtc = runtimeCookieService.GetDefaultExpiry();
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
