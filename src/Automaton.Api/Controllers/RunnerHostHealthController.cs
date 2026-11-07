using Automaton.Api.ServerHealth;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>Where a Machine Management Runner reports host figures with its Runner API key.</summary>
[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/runner-health")]
public sealed class RunnerHostHealthController(HostHealthReportService reports) : ControllerBase
{
    [HttpPost("machine")]
    public async Task<IActionResult> Report([FromBody] MachineHostHealth report, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var apiKeyId)) return Unauthorized();
        try
        {
            await reports.ReportAsync(apiKeyId, report, token);
            return NoContent();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid host health", detail: exception.Message);
        }
    }
}
