using Automaton.Api.Middleware;
using Automaton.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class MultipartUploadAdmissionTests
{
    [Test]
    public async Task MiddlewareRejectsMultipartRequestWhenCapacityIsOccupied()
    {
        using var admission = CreateAdmission(maxConcurrentRequests: 1);
        using var occupiedLease = admission.TryAcquire();
        var nextCalled = false;
        var middleware = new MultipartUploadAdmissionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            admission);
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test";

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
            Assert.That(context.Response.Headers.RetryAfter, Is.EqualTo("1"));
        });
    }

    [Test]
    public async Task MiddlewareReleasesCapacityAfterMultipartRequestCompletes()
    {
        using var admission = CreateAdmission(maxConcurrentRequests: 1);
        var nextCalled = false;
        var middleware = new MultipartUploadAdmissionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            admission);
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test";

        await middleware.InvokeAsync(context);
        using var nextLease = admission.TryAcquire();

        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(nextLease.IsAcquired, Is.True);
        });
    }

    [Test]
    public void RateLimitingOptionsValidation_RejectsNonPositiveMultipartCapacity()
    {
        var result = new RateLimitingOptionsValidation().Validate(
            null,
            new RateLimitingOptions { MaxConcurrentMultipartRequests = 0 });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("MaxConcurrentMultipartRequests"));
    }

    private static MultipartUploadAdmission CreateAdmission(int maxConcurrentRequests) =>
        new(Options.Create(new RateLimitingOptions
        {
            MaxConcurrentMultipartRequests = maxConcurrentRequests
        }));
}
