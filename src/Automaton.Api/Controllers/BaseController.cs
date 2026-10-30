using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseController : ControllerBase
{
    private IMediator? _mediator;

    protected IMediator Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<IMediator>();

    public BaseController()
    {
    }

    protected Guid GetUserId()
    {
        var userIdString = HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        Guid.TryParse(userIdString, out Guid userId);

        return userId;
    }

    protected string? GetCorrelationId()
    {
        return Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
    }
}
