using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace Automaton.Api.Controllers;

public sealed class ActionScheduleExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var status = context.Exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException or JsonException => StatusCodes.Status400BadRequest,
            _ => 0
        };
        if (status == 0) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status404NotFound => "Not found",
                _ => "Invalid schedule"
            },
            Detail = context.Exception.Message
        }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
