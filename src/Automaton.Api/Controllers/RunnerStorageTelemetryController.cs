using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/runner-storage")]
public sealed class RunnerStorageTelemetryController(
    RunnerStorageTelemetrySubmissionService submissionService) : ControllerBase
{
    [HttpPost("hosted")]
    public async Task<IActionResult> ReportHostedRunnerStorage(
        [FromBody] HostedRunnerStorageTelemetry report,
        CancellationToken cancellationToken)
    {
        if (!TryApiKeyId(out var apiKeyId))
            return Unauthorized();

        return await SubmitAsync(
            () => submissionService.ReportHostedRunnerAsync(apiKeyId, report, cancellationToken));
    }

    [HttpPost("host")]
    public async Task<IActionResult> ReportRunnerHostStorage(
        [FromBody] RunnerHostStorageTelemetry report,
        CancellationToken cancellationToken)
    {
        if (!TryApiKeyId(out var apiKeyId))
            return Unauthorized();

        return await SubmitAsync(
            () => submissionService.ReportRunnerHostAsync(apiKeyId, report, cancellationToken));
    }

    private async Task<IActionResult> SubmitAsync(Func<Task> submit)
    {
        try
        {
            await submit();
            return NoContent();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid storage telemetry", detail: exception.Message);
        }
    }

    private bool TryApiKeyId(out Guid apiKeyId) =>
        Guid.TryParse(User.FindFirst("api_key_id")?.Value, out apiKeyId);
}
