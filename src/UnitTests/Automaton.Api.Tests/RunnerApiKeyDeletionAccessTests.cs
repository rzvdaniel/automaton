using System.Security.Claims;
using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Tests;

public sealed class RunnerApiKeyDeletionAccessTests
{
    [Test]
    public void DeveloperKeysCanReachBothRemovalSteps()
    {
        foreach (var action in new[] { nameof(RunnersController.PrepareRemoval), nameof(RunnersController.Delete) })
        {
            var policies = typeof(RunnersController).GetMethod(action)!
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Select(attribute => attribute.Policy);
            Assert.That(policies, Does.Contain("AllowApiKey"));
        }
    }

    [Test]
    public async Task ReadOnlyKeyCannotPrepareOrDeleteRunner()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("api_key_scope", "ReadOnly")], "ApiKey"));
        var controller = new RunnersController(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };

        var prepared = await controller.PrepareRemoval(Guid.NewGuid(), null!, CancellationToken.None);
        var deleted = await controller.Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That((prepared as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That((deleted as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        });
    }
}
