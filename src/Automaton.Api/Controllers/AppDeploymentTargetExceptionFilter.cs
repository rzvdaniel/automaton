using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Automaton.Api.Controllers;

public sealed class AppDeploymentTargetExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var statusCode = context.Exception switch
        {
            KeyNotFoundException => StatusCodes.Status404NotFound,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            AppDeploymentTargetConflictException => StatusCodes.Status409Conflict,
            ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => 0
        };
        if (statusCode == 0)
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = statusCode,
            Title = context.Exception.Message
        })
        {
            StatusCode = statusCode
        };
        context.ExceptionHandled = true;
    }
}
