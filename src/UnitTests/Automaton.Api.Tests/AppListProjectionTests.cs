using Automaton.Api.Services;
using Automaton.Api.Models;
using Automaton.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Automaton.Api.Tests;

public sealed class AppListProjectionTests
{
    [Test]
    public void ParsesPresentationTagsWithoutNormalizingStoredValue()
    {
        Assert.That(AppTagNormalizer.Normalize(" games, strategy ,, "), Is.EqualTo(new[] { "games", "strategy" }));
        Assert.That(AppTagNormalizer.Normalize((string?)null), Is.Empty);
    }

    [Test]
    public void NormalizesTagsWithStableFirstSeenCasingAndOrder()
    {
        var tags = AppTagNormalizer.Normalize(" Games, games, GAMES, strategy ,, Games ");

        Assert.That(tags, Is.EqualTo(new[] { "Games", "strategy" }));
        Assert.That(AppTagNormalizer.Serialize(tags), Is.EqualTo("Games,strategy"));
    }

    [Test]
    public void AcceptsExactTagLimits()
    {
        var maximumLengthTag = new string('x', AppTagNormalizer.MaximumTagLength);
        var maximumTags = Enumerable.Range(0, AppTagNormalizer.MaximumTagsPerApp).Select(index => $"tag-{index}");

        Assert.That(AppTagNormalizer.Normalize(maximumLengthTag), Is.EqualTo(new[] { maximumLengthTag }));
        Assert.That(AppTagNormalizer.Normalize(maximumTags), Has.Count.EqualTo(AppTagNormalizer.MaximumTagsPerApp));
    }

    [Test]
    public void RejectsTagsOverLimits()
    {
        Assert.Throws<AppTagValidationException>(() => AppTagNormalizer.Normalize(new string('x', AppTagNormalizer.MaximumTagLength + 1)));
        Assert.Throws<AppTagValidationException>(() => AppTagNormalizer.Normalize(Enumerable.Range(0, AppTagNormalizer.MaximumTagsPerApp + 1).Select(index => $"tag-{index}")));
    }

    [Test]
    public void SerializesNullOrEmptyTagsAsNull()
    {
        Assert.That(AppTagNormalizer.Serialize((string?)null), Is.Null);
        Assert.That(AppTagNormalizer.Serialize(" , , "), Is.Null);
    }

    [Test]
    public void TagValidationExceptionMapsToBadRequest()
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(actionContext, [])
        {
            Exception = new AppTagValidationException("Tags cannot exceed 40 characters.")
        };

        new GlobalExceptionFilter(NullLogger<GlobalExceptionFilter>.Instance).OnException(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.ExceptionHandled, Is.True);
            Assert.That((context.Result as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        });
    }

    [Test]
    public void TagSettingsRequestAcceptsLegacyStringAndArrayInput()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var legacy = JsonSerializer.Deserialize<UpdateAppTagSettingsRequest>("""{"tags":"Games, strategy"}""", options);
        var modern = JsonSerializer.Deserialize<UpdateAppTagSettingsRequest>("""{"tags":["Games","strategy"]}""", options);

        Assert.That(legacy!.Tags, Is.EqualTo(new[] { "Games, strategy" }));
        Assert.That(modern!.Tags, Is.EqualTo(new[] { "Games", "strategy" }));
    }
}
