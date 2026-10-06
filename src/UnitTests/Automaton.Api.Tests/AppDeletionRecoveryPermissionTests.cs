using System.Security.Claims;
using Automaton.Api.Controllers;
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
}
