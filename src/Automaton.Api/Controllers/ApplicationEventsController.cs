using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps/{appId:guid}/application-events")]
[Authorize]
public class ApplicationEventsController : ControllerBase
{
    private readonly IApplicationEventService applicationEventService;
    private readonly AppsService appsService;

    public ApplicationEventsController(
        IApplicationEventService applicationEventService,
        AppsService appsService)
    {
        this.applicationEventService = applicationEventService;
        this.appsService = appsService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ApplicationEventDto>>> GetEvents(
        Guid appId,
        [FromQuery] string? category = null,
        [FromQuery] string? level = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);
        if (app == null)
        {
            return NotFound(new { message = "App not found" });
        }

        var result = await applicationEventService.GetEventsAsync(
            appId,
            new ApplicationEventQuery
            {
                Category = category,
                Level = level,
                From = from,
                To = to,
                Page = Math.Max(1, page),
                PageSize = Math.Min(200, Math.Max(1, pageSize)),
            },
            cancellationToken);

        return Ok(result);
    }
}
