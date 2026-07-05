using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/documentation")]
[Authorize(Policy = "Administrators")]
public sealed class AdminDocumentationController : ControllerBase
{
    private readonly AdminDocumentationService _service;

    public AdminDocumentationController(AdminDocumentationService service)
    {
        _service = service;
    }

    [HttpGet]
    public ActionResult<IEnumerable<AdminDocumentationTopicListItemResponse>> ListTopics()
    {
        return Ok(_service.ListTopics());
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<AdminDocumentationTopicResponse>> GetTopic(string key, CancellationToken cancellationToken)
    {
        var topic = await _service.GetTopicAsync(key, cancellationToken);
        return topic is null ? NotFound() : Ok(topic);
    }
}
