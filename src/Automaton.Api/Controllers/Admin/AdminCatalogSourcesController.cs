using Automaton.Api.Constants;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

/// <summary>Catalog sources (GitHub repositories) of this instance. A system administrator manages them; the token of a private one is never returned.</summary>
[ApiController]
[Route("api/admin/catalog/sources")]
[Authorize(Roles = UserRole.SystemAdministrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCatalogSourcesController(CatalogSourceStore sources, AppCatalogService catalog, IAuditEventService audit) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CatalogSourceDto>>> List(CancellationToken cancellationToken) =>
        Ok(await sources.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CatalogSourceDto>> Add([FromBody] CatalogSourceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var candidate = CatalogSourceStore.Candidate(request.Repo, request.Branch, request.Name, request.Token);
            await sources.EnsureAddableAsync(candidate, cancellationToken);
            var (problem, appCount) = await catalog.ProbeAsync(candidate, cancellationToken);
            if (problem is not null) return BadRequest(new { message = problem });
            var added = await sources.AddAsync(candidate, GetUserId(), cancellationToken);
            await Audit(AuditActions.Added, added, cancellationToken);
            return Created($"/api/admin/catalog/sources/{added.Id}", added with { AppCount = appCount });
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPut("{id}/enabled")]
    public async Task<ActionResult<CatalogSourceDto>> SetEnabled(string id, [FromBody] CatalogSourceEnabledRequest request, CancellationToken cancellationToken)
    {
        var updated = await sources.SetEnabledAsync(id, request.Enabled, cancellationToken);
        if (updated is null) return NotFound();
        await Audit(request.Enabled ? AuditActions.Enabled : AuditActions.Disabled, updated, cancellationToken);
        return Ok(updated);
    }

    /// <summary>Removes the source and its saved catalog. Apps already installed from it keep running.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id, CancellationToken cancellationToken)
    {
        var removed = await sources.RemoveAsync(id, cancellationToken);
        if (removed is null) return NotFound();
        await Audit(AuditActions.Removed, removed, cancellationToken);
        return NoContent();
    }

    private Task Audit(string action, CatalogSourceDto source, CancellationToken cancellationToken) =>
        audit.WriteAsync(action, AuditEntityTypes.CatalogSource, null, $"Catalog source '{source.Repo}' was {action.ToLowerInvariant()}",
            new { source.Repo, source.Branch, source.HasToken, ActorUserId = GetUserId() }, cancellationToken: cancellationToken);
}
