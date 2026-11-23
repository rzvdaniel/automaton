using Automaton.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PermissionsAdminForbiddenHandlerTests
{
    private static async Task<(int Status, string Body, bool NextCalled)> RunAsync(string? policyName, PolicyAuthorizationResult result)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddAuthentication().Services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        var metadata = policyName is null ? new EndpointMetadataCollection() : new EndpointMetadataCollection(new AuthorizeAttribute { Policy = policyName });
        context.SetEndpoint(new Endpoint(null, metadata, "test"));
        var next = false;
        await new PermissionsAdminForbiddenHandler().HandleAsync(_ => { next = true; return Task.CompletedTask; }, context,
            new AuthorizationPolicyBuilder().RequireAssertion(_ => false).Build(), result);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(), next);
    }

    [Test]
    public async Task ForbiddenOnTheApprovalPolicy_Is403WithTheReason()
    {
        var (status, body, next) = await RunAsync(PermissionsAdminKey.PolicyName, PolicyAuthorizationResult.Forbid());
        Assert.That(status, Is.EqualTo(403));
        Assert.That(body, Does.Contain("permissions-admin"));
        Assert.That(next, Is.False);
    }

    [Test]
    public void ForbiddenElsewhere_GoesToTheDefaultHandler()
        // The default handler forbids through the authentication service; with no scheme configured it throws, which proves it ran.
        => Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync("AllowApiKey", PolicyAuthorizationResult.Forbid()));

    [Test]
    public async Task Allowed_ContinuesTheRequest()
    {
        var (_, _, next) = await RunAsync(PermissionsAdminKey.PolicyName, PolicyAuthorizationResult.Success());
        Assert.That(next, Is.True);
    }
}
