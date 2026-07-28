using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Automaton.Api.Services;

public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> logger;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
    {
        this.logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is AppTagValidationException exception)
        {
            context.Result = new BadRequestObjectResult(new { message = exception.Message });
            context.ExceptionHandled = true;
            return;
        }

        logger.LogError(context.Exception, "An unhandled exception occurred.");

        context.Result = new ObjectResult(new { Message = "An unexpected error occurred." })
        {
            StatusCode = 500
        };

        context.ExceptionHandled = true;
    }
}
