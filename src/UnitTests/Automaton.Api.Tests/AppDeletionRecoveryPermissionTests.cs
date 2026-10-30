using System.Security.Claims;
using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionRecoveryPermissionTests
{
    [Test]
    public async Task Recovery_RequiresAdministratorBeforeAccessingDeletionService()
    {
        var controller = new AppsController(null!, NullLogger<AppsController>.Instance, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "test"))
                }
            }
        };
        Assert.That(await controller.Delete(Guid.NewGuid(), recoverInterrupted: true), Is.TypeOf<ForbidResult>());
    }

    [Test]
    public async Task OfflineRuntimeAbandonment_RequiresAdministratorAndRecovery()
    {
        var controller = new AppsController(null!, NullLogger<AppsController>.Instance, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "test"))
                }
            }
        };
        Assert.That(await controller.Delete(Guid.NewGuid(), recoverInterrupted: true, abandonOfflineRuntimes: true),
            Is.TypeOf<ForbidResult>());

        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
        Assert.That(await controller.Delete(Guid.NewGuid(), abandonOfflineRuntimes: true), Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public void Delete_AcceptsTheExistingApiKeyPolicy()
    {
        var method = typeof(AppsController).GetMethod(nameof(AppsController.Delete))!;
        Assert.That(method.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>().Any(attribute => attribute.Policy == "AllowApiKey"), Is.True);
    }

    [Test]
    public async Task Delete_RejectsReadOnlyApiKey()
    {
        var controller = new AppsController(null!, NullLogger<AppsController>.Instance, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Role, "Admin"), new Claim("api_key_scope", "ReadOnly")], "test"))
                }
            }
        };
        var result = await controller.Delete(Guid.NewGuid()) as ObjectResult;
        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }
}
