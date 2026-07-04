using System.ComponentModel.DataAnnotations;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/ai-guidance")]
[Authorize(Policy = "Administrators")]
public sealed class AdminAiGuidanceController : ControllerBase
{
    private readonly AdminAiGuidanceService _service;

    public AdminAiGuidanceController(AdminAiGuidanceService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiGuidanceAdminResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var documents = await _service.ListAsync(cancellationToken);
        return Ok(documents.Select(Map));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AiGuidanceAdminResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var document = await _service.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        return Ok(Map(document));
    }

    [HttpPost]
    public async Task<ActionResult<AiGuidanceAdminResponse>> Create(
        [FromBody] UpsertAiGuidanceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await _service.CreateAsync(
                request.Key,
                request.Title,
                request.Surface,
                request.DocumentKind,
                request.Description,
                request.TagsJson,
                request.ContentMarkdown,
                request.IsEnabled,
                request.RequiredByDefault,
                request.IsModelLoadable,
                request.SortOrder,
                cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = document.Id }, Map(document));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AiGuidanceAdminResponse>> Update(
        Guid id,
        [FromBody] UpsertAiGuidanceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await _service.UpdateAsync(
                id,
                request.Key,
                request.Title,
                request.Surface,
                request.DocumentKind,
                request.Description,
                request.TagsJson,
                request.ContentMarkdown,
                request.IsEnabled,
                request.RequiredByDefault,
                request.IsModelLoadable,
                request.SortOrder,
                cancellationToken);

            if (document is null)
            {
                return NotFound();
            }

            return Ok(Map(document));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/enable")]
    public async Task<ActionResult<AiGuidanceAdminResponse>> Enable(Guid id, CancellationToken cancellationToken)
    {
        var document = await _service.SetEnabledAsync(id, true, cancellationToken);
        return document is null ? NotFound() : Ok(Map(document));
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<ActionResult<AiGuidanceAdminResponse>> Disable(Guid id, CancellationToken cancellationToken)
    {
        var document = await _service.SetEnabledAsync(id, false, cancellationToken);
        return document is null ? NotFound() : Ok(Map(document));
    }

    [HttpPost("{id:guid}/reset")]
    public async Task<ActionResult<AiGuidanceAdminResponse>> Reset(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _service.ResetToEmbeddedDefaultAsync(id, cancellationToken);
            return document is null ? NotFound() : Ok(Map(document));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private AiGuidanceAdminResponse Map(Automaton.Api.Entities.AiGuidanceDocument document) => new()
    {
        Id = document.Id,
        Key = document.Key,
        Title = document.Title,
        Surface = document.Surface,
        DocumentKind = document.DocumentKind,
        Description = document.Description,
        TagsJson = document.TagsJson,
        ContentMarkdown = document.ContentMarkdown,
        IsEnabled = document.IsEnabled,
        RequiredByDefault = document.RequiredByDefault,
        IsModelLoadable = document.IsModelLoadable,
        SortOrder = document.SortOrder,
        Version = document.Version,
        CreatedAt = document.CreatedAt,
        UpdatedAt = document.UpdatedAt,
        HasEmbeddedDefault = _service.HasEmbeddedDefault(document.Key)
    };
}

public sealed class AiGuidanceAdminResponse
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
    public string DocumentKind { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? TagsJson { get; set; }
    public string ContentMarkdown { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool RequiredByDefault { get; set; }
    public bool IsModelLoadable { get; set; }
    public int SortOrder { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool HasEmbeddedDefault { get; set; }
}

public sealed class UpsertAiGuidanceRequest
{
    [Required]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Surface { get; set; } = string.Empty;

    [Required]
    public string DocumentKind { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? TagsJson { get; set; }

    [Required]
    public string ContentMarkdown { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public bool RequiredByDefault { get; set; }
    public bool IsModelLoadable { get; set; } = true;
    public int SortOrder { get; set; } = 100;
}
