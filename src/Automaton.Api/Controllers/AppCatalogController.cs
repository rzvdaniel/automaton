using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>The app catalog (read from the official automaton-apps repository) and installing from it or from a Git URL.</summary>
// A development API key (the scope automaton login creates) may list and install catalog apps, so the CLI and AI agents can do what Studio does; read-only, runner and web-app keys are refused.
[Authorize(Policy = "DeveloperApiAccess")]
public class CatalogController(AppCatalogService catalog, AppCatalogInstallService installer) : BaseController
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
}
