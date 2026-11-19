using Automaton.Api.Extensions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Serilog.Events;

namespace Automaton.Api.Tests;

[TestFixture]
public class ApiRequestLoggingLevelTests
{
    private static HttpContext Context(string path, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;
        return context;
    }

    
    [TestCase("/internal/runtime/auth", 401, 5, LogEventLevel.Information)]
    [TestCase("/internal/runtime/auth", 403, 5, LogEventLevel.Information)]
    [TestCase("/api/runner-tunnel/connect", 101, 600000, LogEventLevel.Debug)]
    [TestCase("/.env", 404, 5, LogEventLevel.Warning)]
    [TestCase("/internal/runtime/auth", 500, 5, LogEventLevel.Error)]
    [TestCase("/api/apps", 200, 5000, LogEventLevel.Warning)]
    public void GetLevel_matches_expected(string path, int status, double elapsed, LogEventLevel expected)
    {
        Assert.That(ApiRequestLoggingMiddleware.GetLevel(Context(path, status), elapsed, null, false, false), Is.EqualTo(expected));
    }
}
