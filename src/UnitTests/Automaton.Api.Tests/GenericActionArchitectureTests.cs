using Automaton.Api.Controllers;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class GenericActionArchitectureTests
{
    [Test]
    public void CanonicalActionServiceOwnsManualAndScheduledExecution()
    {
        var methods = typeof(AppActionExecutionService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .ToArray();

        Assert.That(methods, Does.Contain(nameof(AppActionExecutionService.ExecuteAsync)));
        Assert.That(methods, Does.Contain(nameof(AppActionExecutionService.ExecuteScheduledAsync)));
    }

}
