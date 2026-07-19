using Automaton.Api.Controllers;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Automaton.Api.Tests;

[TestFixture]
public class CliAuthorizationTests
{
    [Test]
    public void RequestCreationAndPollingAreAnonymous_ApprovalRequiresAuthentication()
    {
        var controller = typeof(CliAuthorizationController);
        var create = controller.GetMethod(nameof(CliAuthorizationController.CreateRequest));
        var approve = controller.GetMethod(nameof(CliAuthorizationController.Approve));
        var poll = controller.GetMethod(nameof(CliAuthorizationController.Poll));
        var details = controller.GetMethod(nameof(CliAuthorizationController.GetDetails));
        var complete = controller.GetMethod(nameof(CliAuthorizationController.Complete));

        Assert.That(create!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true), Is.Not.Empty);
        Assert.That(poll!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true), Is.Not.Empty);
        Assert.That(approve!.GetCustomAttributes(typeof(AuthorizeAttribute), true), Is.Not.Empty);
        Assert.That(details!.GetCustomAttributes(typeof(AuthorizeAttribute), true), Is.Not.Empty);
        Assert.That(complete!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true), Is.Not.Empty);
    }

    [Test]
    public async Task ApprovalRejectsUnconfirmedEmailBeforeAuthorizingRequest()
    {
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
                    "test"))
        };
        var userContext = new UserContextService(new HttpContextAccessor { HttpContext = httpContext });
        var controller = new CliAuthorizationController(
            null!,
            null!,
            userContext,
            new UnconfirmedEmailGuard());

        var result = await controller.Approve(
            Guid.NewGuid(),
            new ApproveCliAuthorizationRequest { UserCode = "ABCD-EFGH" },
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    private sealed class UnconfirmedEmailGuard : ConfirmedEmailGuardService
    {
        public UnconfirmedEmailGuard() : base(null!) { }

        public override Task<bool> IsConfirmedAsync(Guid userId) => Task.FromResult(false);
    }
}
