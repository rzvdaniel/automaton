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

    [Test]
    public void LegacyControllerServiceHasNoActionExecutionOrControllerTunnelEntryPoint()
    {
        var methods = typeof(AppsControllerExecutionService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(method => method.Name)
            .ToArray();

        Assert.That(methods, Does.Not.Contain("ExecuteActionAsync"));
        Assert.That(methods, Does.Not.Contain("ExecuteViaTunnelAsync"));
        Assert.That(methods, Does.Not.Contain("DeployScriptsToRunnerAsync"));
    }

    [Test]
    public void ControllerActionRoutesAreExplicitlyRetiredUntilStage2C()
    {
        var methods = typeof(AppsControllerExecutionController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name is "ExecuteAction" or "ExecuteActionPreview" or "ExecuteActionPrivate")
            .ToArray();

        Assert.That(methods, Has.Length.EqualTo(3));
        Assert.That(methods, Has.All.Property("ReturnType").EqualTo(typeof(ActionResult<AppControllerActionResponse>)));

        var controller = new AppsControllerExecutionController(
            null!, null!, null!, null!, null!, null!, null!, null!);

        Assert.That(controller.ExecuteAction("app", "controller", "action", null!, CancellationToken.None).Result,
            Is.TypeOf<ObjectResult>().With.Property("StatusCode").EqualTo(StatusCodes.Status410Gone));
        Assert.That(controller.ExecuteActionPreview(Guid.NewGuid(), "controller", "action", null!, CancellationToken.None).Result,
            Is.TypeOf<ObjectResult>().With.Property("StatusCode").EqualTo(StatusCodes.Status410Gone));
        Assert.That(controller.ExecuteActionPrivate(Guid.NewGuid(), "controller", "action", null!, CancellationToken.None).Result,
            Is.TypeOf<ObjectResult>().With.Property("StatusCode").EqualTo(StatusCodes.Status410Gone));
    }
}
