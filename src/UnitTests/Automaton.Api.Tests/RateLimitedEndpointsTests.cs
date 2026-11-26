using Automaton.Api.Constants;
using Automaton.Api.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;

namespace Automaton.Api.Tests;

[TestFixture]
public class RateLimitedEndpointsTests
{
    [TestCase(typeof(InvitationsController), "Validate", RateLimitPolicyNames.InvitationAccept)]
    [TestCase(typeof(InvitationsController), "Accept", RateLimitPolicyNames.InvitationAccept)]
    [TestCase(typeof(InvitationsController), "Complete", RateLimitPolicyNames.InvitationAccept)]
    [TestCase(typeof(CliAuthorizationController), "Approve", RateLimitPolicyNames.CliApproval)]
    public void Endpoint_IsLimitedPerAddressByItsPolicy(Type controller, string action, string policy)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);

        Assert.That(method, Is.Not.Null);
        Assert.That(method!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName, Is.EqualTo(policy));
    }
}
