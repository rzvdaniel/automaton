using Automaton.Api.Constants;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Automaton.Api.Controllers;

/// <summary>
/// Starts an Action from outside Automaton. No sign-in: the secret in <c>Authorization: Bearer</c> or
/// <c>X-Automaton-Webhook-Secret</c> is the credential, and a call without a valid one gets 404.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/hooks")]
public sealed class ActionWebhookCallsController(ActionWebhookService webhooks) : ControllerBase
{
    [HttpPost("{id:guid}")]
    [EnableRateLimiting(RateLimitPolicyNames.ActionWebhookCall)]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Call(Guid id, CancellationToken ct)
    {
        var secret = ReadSecret();
        // An unknown secret must not make us read a large body first.
        if (string.IsNullOrEmpty(secret)) return StatusCode(StatusCodes.Status404NotFound, new { message = "Not found." });
        if (Request.ContentLength > ActionWebhookService.MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = $"The body can be at most {ActionWebhookService.MaxBodyBytes / 1024} KB." });
        var body = await ReadBodyAsync(ct);
        if (body == null)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = $"The body can be at most {ActionWebhookService.MaxBodyBytes / 1024} KB." });
        var result = await webhooks.CallAsync(id, secret, body, ct);
        return StatusCode(result.StatusCode, new { message = result.Message, executionId = result.ExecutionId });
    }

    private string? ReadSecret()
    {
        var header = Request.Headers["X-Automaton-Webhook-Secret"].ToString();
        if (!string.IsNullOrWhiteSpace(header)) return header.Trim();
        var auth = Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Trim() : null;
    }

    private async Task<string?> ReadBodyAsync(CancellationToken ct)
    {
        var buffer = new byte[ActionWebhookService.MaxBodyBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await Request.Body.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }
        return read > ActionWebhookService.MaxBodyBytes ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }
}
