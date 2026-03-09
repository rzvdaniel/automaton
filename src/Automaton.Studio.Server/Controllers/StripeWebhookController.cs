using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Receives Stripe webhook events. No auth — verified by Stripe signature.
/// Register this URL in the Stripe dashboard: POST https://yourapp.com/api/webhooks/stripe
/// </summary>
[ApiController]
[Route("api/webhooks/stripe")]
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
    public async Task<IActionResult> Handle()
    {
        var payload = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrEmpty(signature))
            return BadRequest("Missing Stripe-Signature header");

        try
        {
            await _stripe.HandleWebhookAsync(payload, signature);
            return Ok();
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook rejected: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe webhook processing failed");
            return StatusCode(500);
        }
    }
}
