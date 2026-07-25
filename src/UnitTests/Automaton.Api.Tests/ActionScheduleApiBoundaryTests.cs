using Automaton.Api.Controllers;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ActionScheduleApiBoundaryTests
{
    [TestCase("2026-01-15T12:00:00")]
    [TestCase("not-a-date")]
    public void RunAtUtc_RejectsTimestampWithoutExplicitOffset(string value)
        => Assert.That(
            () => JsonSerializer.Deserialize<SaveActionScheduleRequest>($$"""{"runAtUtc":"{{value}}"}""", new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Throws.TypeOf<JsonException>());

    [TestCase("2026-01-15T12:00:00Z")]
    [TestCase("2026-01-15T12:00:00+02:00")]
    public void RunAtUtc_AcceptsTimestampWithExplicitOffset(string value)
        => Assert.That(
            JsonSerializer.Deserialize<SaveActionScheduleRequest>($$"""{"runAtUtc":"{{value}}"}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!.RunAtUtc,
            Is.Not.Null);

    [TestCase(typeof(InvalidOperationException), StatusCodes.Status400BadRequest)]
    [TestCase(typeof(UnauthorizedAccessException), StatusCodes.Status403Forbidden)]
    [TestCase(typeof(KeyNotFoundException), StatusCodes.Status404NotFound)]
    public void ExceptionFilter_MapsExpectedServiceErrors(Type exceptionType, int expectedStatus)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(actionContext, [])
        {
            Exception = (Exception)Activator.CreateInstance(exceptionType, "failure")!
        };
        new ActionScheduleExceptionFilter().OnException(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.ExceptionHandled, Is.True);
            Assert.That((context.Result as ObjectResult)?.StatusCode, Is.EqualTo(expectedStatus));
        });
    }
}
