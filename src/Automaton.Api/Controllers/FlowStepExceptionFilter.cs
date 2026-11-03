using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Automaton.Api.Controllers;

/// <summary>Answers a refused flow or custom-step request with 403, 404 or 400 and its message instead of a generic 500.</summary>
public sealed class FlowStepExceptionFilter(ILogger<FlowStepExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var statusCode = context.Exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => 0
        };
        if (statusCode == 0) return;

        // Handled here, so the global filter that logs unexpected errors does not see it.
        logger.LogWarning(context.Exception, "Flow or custom step request refused with {StatusCode}", statusCode);
        context.Result = new ObjectResult(new { message = context.Exception.Message }) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }
}
