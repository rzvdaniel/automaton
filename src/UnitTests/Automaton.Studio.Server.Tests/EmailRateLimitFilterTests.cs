using Automaton.Api.Application.Commands;
using Automaton.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class EmailRateLimitFilterTests
{
    private IMemoryCache memoryCache = null!;

    [SetUp]
    public void SetUp()
    {
        memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    [TearDown]
    public void TearDown()
    {
        memoryCache.Dispose();
    }

    [Test]
    public async Task OnActionExecutionAsync_AllowsRequestsUpToLimit()
    {
        var filter = CreateFilter(permitLimit: 2);
        var firstContext = CreateContext(new SignInUserCommand("user@example.com", "pw"));
        var secondContext = CreateContext(new SignInUserCommand("user@example.com", "pw"));

        await filter.OnActionExecutionAsync(firstContext, () => Task.FromResult(CreateExecutedContext(firstContext)));
        await filter.OnActionExecutionAsync(secondContext, () => Task.FromResult(CreateExecutedContext(secondContext)));

        Assert.That(firstContext.Result, Is.Null);
        Assert.That(secondContext.Result, Is.Null);
    }

    [Test]
    public async Task OnActionExecutionAsync_BlocksRequestAfterLimit()
    {
        var filter = CreateFilter(permitLimit: 1);
        var firstContext = CreateContext(new SignInUserCommand("user@example.com", "pw"));
        var secondContext = CreateContext(new SignInUserCommand("user@example.com", "pw"));

        await filter.OnActionExecutionAsync(firstContext, () => Task.FromResult(CreateExecutedContext(firstContext)));
        await filter.OnActionExecutionAsync(secondContext, () => Task.FromResult(CreateExecutedContext(secondContext)));

        Assert.That(firstContext.Result, Is.Null);
        Assert.That(secondContext.Result, Is.TypeOf<ObjectResult>());

        var result = (ObjectResult)secondContext.Result!;
        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(secondContext.HttpContext.Response.Headers.RetryAfter.ToString(), Is.EqualTo("60"));
    }

    [Test]
    public async Task OnActionExecutionAsync_NormalizesEmailCasingAndWhitespace()
    {
        var filter = CreateFilter(permitLimit: 1);
        var firstContext = CreateContext(new SignInUserCommand(" User@Example.com ", "pw"));
        var secondContext = CreateContext(new SignInUserCommand("user@example.com", "pw"));

        await filter.OnActionExecutionAsync(firstContext, () => Task.FromResult(CreateExecutedContext(firstContext)));
        await filter.OnActionExecutionAsync(secondContext, () => Task.FromResult(CreateExecutedContext(secondContext)));

        Assert.That(secondContext.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)secondContext.Result!).StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
    }

    [Test]
    public async Task OnActionExecutionAsync_IgnoresArgumentsWithoutEmailProperty()
    {
        var filter = CreateFilter(permitLimit: 1);
        var context = CreateContext(new { Name = "No email" });

        await filter.OnActionExecutionAsync(context, () => Task.FromResult(CreateExecutedContext(context)));

        Assert.That(context.Result, Is.Null);
    }

    private EmailRateLimitFilter CreateFilter(int permitLimit)
    {
        return new EmailRateLimitFilter(
            memoryCache,
            NullLogger<EmailRateLimitFilter>.Instance,
            "test:email",
            permitLimit,
            60);
    }

    private static ActionExecutingContext CreateContext(object actionArgument)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor(), new ModelStateDictionary());
        var actionArguments = new Dictionary<string, object?> { ["command"] = actionArgument };

        return new ActionExecutingContext(
            actionContext,
            [],
            actionArguments,
            controller: new object());
    }

    private static ActionExecutedContext CreateExecutedContext(ActionExecutingContext context)
    {
        return new ActionExecutedContext(context, [], context.Controller);
    }
}
