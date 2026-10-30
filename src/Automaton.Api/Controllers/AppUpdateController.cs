using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps")]
public sealed class AppUpdateController(AppsService appsService) : ControllerBase
{
    [HttpPut("{id}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Put(Guid id, UpdateAppRequest request)
    {
        try
        {
            await appsService.Update(id, request.ToAppModel());
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        return NoContent();
    }
}
