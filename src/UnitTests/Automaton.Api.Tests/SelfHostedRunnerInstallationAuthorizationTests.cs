using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Controllers;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Tests;

public sealed class SelfHostedRunnerInstallationAuthorizationTests
{
    [Test]
    public async Task MissingHostAccessAcknowledgementReturnsBadRequest()
    {
        await using var db = SelfHostedRunnerInstallationTestServices.CreateDbContext();
        var controller = new SelfHostedRunnersController(
            SelfHostedRunnerInstallationTestServices.CreateService(db, Guid.NewGuid()));
        var result = await controller.CreateInstallation(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "test-runner",
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement
        }, CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That(result.Result is BadRequestObjectResult badRequest &&
            badRequest.Value?.ToString()?.Contains("Acknowledge Docker host access for the Machine Management profile", StringComparison.Ordinal) == true,
            Is.True);
    }
    [Test]
    public void IdentityAndDeveloperCredentialsCanCreateInstallations()
    {
        Assert.That(SelfHostedRunnerInstallationAuthorization.IsAllowed(CreatePrincipal(null)), Is.True);
        Assert.That(
            SelfHostedRunnerInstallationAuthorization.IsAllowed(CreatePrincipal(ApiKeyScope.Developer)),
            Is.True);
    }

    [TestCase(ApiKeyScope.ReadOnly)]
    [TestCase(ApiKeyScope.WorkflowExecution)]
    [TestCase(ApiKeyScope.Runner)]
    [TestCase(ApiKeyScope.WebApp)]
    [TestCase((ApiKeyScope)6)]
    [TestCase(ApiKeyScope.FullAccess)]
    public void NonDeveloperApiKeysCannotCreateInstallations(ApiKeyScope scope)
    {
        Assert.That(SelfHostedRunnerInstallationAuthorization.IsAllowed(CreatePrincipal(scope)), Is.False);
    }

    [Test]
    public void InstallationEndpointUsesFocusedAuthorizationPolicy()
    {
        var attribute = typeof(SelfHostedRunnersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.That(attribute.Policy, Is.EqualTo(SelfHostedRunnerInstallationAuthorization.PolicyName));
    }

    [Test]
    public void InstallationStatusEndpointUsesTheSameFocusedAuthorizationPolicy()
    {
        var attribute = typeof(SelfHostedRunnersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.That(attribute.Policy, Is.EqualTo(SelfHostedRunnerInstallationAuthorization.PolicyName));
    }

    private static ClaimsPrincipal CreatePrincipal(ApiKeyScope? scope)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (scope.HasValue)
            claims.Add(new Claim("api_key_scope", scope.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
