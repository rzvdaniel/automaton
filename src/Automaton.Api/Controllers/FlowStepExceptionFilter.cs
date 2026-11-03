using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Automaton.Api.Controllers;

/// <summary>Answers a refused flow or custom-step request with 403 or 400 and its message instead of a generic 500.</summary>
public sealed class FlowStepExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var statusCode = context.Exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => 0
        };
        if (statusCode == 0) return;

        context.Result = new ObjectResult(new { message = context.Exception.Message }) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }
}
