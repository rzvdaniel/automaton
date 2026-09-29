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

        await submissionService.ReportHostedRunnerAsync(apiKeyId, report, cancellationToken);
        return NoContent();
    }

    [HttpPost("host")]
    public async Task<IActionResult> ReportRunnerHostStorage(
        [FromBody] RunnerHostStorageTelemetry report,
        CancellationToken cancellationToken)
    {
        if (!TryApiKeyId(out var apiKeyId))
            return Unauthorized();

        await submissionService.ReportRunnerHostAsync(apiKeyId, report, cancellationToken);
        return NoContent();
    }

    private bool TryApiKeyId(out Guid apiKeyId) =>
        Guid.TryParse(User.FindFirst("api_key_id")?.Value, out apiKeyId);
}
