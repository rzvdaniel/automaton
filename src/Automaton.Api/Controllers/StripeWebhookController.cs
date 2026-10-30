using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace Automaton.Api.Controllers;

/// <summary>
/// Receives Stripe webhook events. No auth — verified by Stripe signature.
/// Register this URL in the Stripe dashboard: POST https://yourapp.com/api/webhooks/stripe
/// </summary>
[ApiController]
[Route("api/webhooks/stripe")]
[AllowAnonymous]
public class StripeWebhookController : ControllerBase
{
    private readonly StripeService _stripe;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(StripeService stripe, ILogger<StripeWebhookController> logger)
    {
        _stripe = stripe;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(
        [FromHeader(Name = "Stripe-Signature")] string? signature,
        CancellationToken cancellationToken)
    {
        // Stripe signs the exact request bytes. Model-binding the payload could change
        // its representation, so this endpoint intentionally reads the raw body.
        using var payloadReader = new StreamReader(HttpContext.Request.Body, leaveOpen: true);
        var payload = await payloadReader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrEmpty(signature))
            return BadRequest("Missing Stripe-Signature header");

        try
        {
            await _stripe.HandleWebhookAsync(payload, signature);
            return Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook rejected: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe webhook processing failed");
            return StatusCode(500);
        }
    }
}
