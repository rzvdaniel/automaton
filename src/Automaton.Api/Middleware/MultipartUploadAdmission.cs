using System.Threading.RateLimiting;
using Automaton.Api.Security;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Middleware;

public sealed class MultipartUploadAdmission : IDisposable
{
    private readonly ConcurrencyLimiter limiter;

    public MultipartUploadAdmission(IOptions<RateLimitingOptions> options)
    {
        limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.Value.MaxConcurrentMultipartRequests,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    }

    public RateLimitLease TryAcquire() => limiter.AttemptAcquire();

    public void Dispose() => limiter.Dispose();
}

public sealed class MultipartUploadAdmissionMiddleware(
    RequestDelegate next,
    MultipartUploadAdmission admission)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMultipartRequest(context.Request.ContentType))
        {
            await next(context);
            return;
        }

        using var lease = admission.TryAcquire();
        if (!lease.IsAcquired)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsJsonAsync(
                new { message = "Multipart upload capacity is busy. Please retry shortly." },
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsMultipartRequest(string? contentType) =>
        contentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true;
}
