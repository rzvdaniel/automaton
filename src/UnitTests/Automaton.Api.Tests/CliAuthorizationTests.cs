using Automaton.Api.Controllers;
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

        Assert.That(create!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true), Is.Not.Empty);
        Assert.That(poll!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true), Is.Not.Empty);
        Assert.That(approve!.GetCustomAttributes(typeof(AuthorizeAttribute), true), Is.Not.Empty);
    }
}
