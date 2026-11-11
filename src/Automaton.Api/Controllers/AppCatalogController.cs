using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>The app catalog (read from the official automaton-apps repository) and installing from it or from a Git URL.</summary>
// A development API key (the scope automaton login creates) may list and install catalog apps, so the CLI and AI agents can do what Studio does; read-only, runner and web-app keys are refused.
[Authorize(Policy = "DeveloperApiAccess")]
public class CatalogController(AppCatalogService catalog, AppCatalogInstallService installer, AppCatalogUpdateService updater) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<CatalogResponse>> Get([FromQuery] CatalogQuery query, [FromQuery] bool refresh = false, CancellationToken cancellationToken = default) =>
        Ok(await catalog.GetAsync(GetUserId(), refresh, cancellationToken, query));

    [HttpGet("resolve")]
    public async Task<ActionResult<GitAppSource>> Resolve([FromQuery] string url, CancellationToken cancellationToken)
    {
        try { return Ok(await installer.ResolveUrlAsync(url, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpGet("install-source")]
    public async Task<ActionResult<CatalogInstallSourceModel>> InstallSource(
        [FromQuery] string repo, [FromQuery] string commit, [FromQuery] string? folder, CancellationToken cancellationToken)
    {
        try { return Ok(await installer.GetSourceAsync(AppCatalogInstallService.Validate(repo, folder, commit), cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPost("install")]
    public async Task<ActionResult<AppModel>> Install([FromBody] CatalogInstallRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var app = await installer.InstallAsync(request, cancellationToken);
            return Created($"/api/apps/{app.Id}", app);
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    /// <summary>Whether the catalog pins a newer commit than the one this app was installed from, and what would change.</summary>
    [HttpGet("apps/{appId:guid}/update")]
    public async Task<ActionResult<AppCatalogUpdateModel>> GetUpdate(Guid appId, CancellationToken cancellationToken)
    {
        try { return Ok(await updater.GetAsync(appId, cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    /// <summary>Replaces the app's files with the catalog's newer commit and redeploys it; data, settings, secrets and schedules stay.</summary>
    [HttpPost("apps/{appId:guid}/update")]
    public async Task<ActionResult<DeploymentOperationResponse>> Update(Guid appId, [FromBody] AppCatalogUpdateRequest request, CancellationToken cancellationToken)
    {
        try { return Accepted(await updater.UpdateAsync(appId, request ?? new AppCatalogUpdateRequest(), cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException exception) { return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message }); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
        { return BadRequest(new { message = exception.Message }); }
    }
}
