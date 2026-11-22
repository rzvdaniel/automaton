using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppPermissionPolicyTests
{
    private static async Task<(AuthorizationPolicy Policy, IAuthorizationService Service)> BuildAsync()
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization(options =>
            AppPermissionAuthenticationOptions.AddPermissionPolicy(options, "p", "backups:read")).BuildServiceProvider();
        var policy = (await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync("p"))!;
        return (policy, services.GetRequiredService<IAuthorizationService>());
    }

    private static ClaimsPrincipal User(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));

    [Test]
    public async Task ApiKeySignInIsAccepted()
    {
        var (policy, _) = await BuildAsync();
        Assert.That(policy.AuthenticationSchemes, Does.Contain(ApiKeyAuthenticationSchemeOptions.DefaultScheme));
    }

    [Test]
    public async Task AdministratorApiKeyOfDeveloperScopeIsAllowed()
    {
        var (policy, service) = await BuildAsync();
        var user = User(new Claim(ClaimTypes.Role, UserRole.SystemAdministrator), new Claim("api_key_scope", "Developer"));
        Assert.That((await service.AuthorizeAsync(user, null, policy)).Succeeded, Is.True);
    }

    [Test]
    public async Task AdministratorApiKeyOfOtherScopeIsRefused()
    {
        var (policy, service) = await BuildAsync();
        var user = User(new Claim(ClaimTypes.Role, UserRole.SystemAdministrator), new Claim("api_key_scope", "Runner"));
        Assert.That((await service.AuthorizeAsync(user, null, policy)).Succeeded, Is.False);
    }

    [Test]
    public async Task AppWithPermissionIsAllowedAndOtherUsersAreRefused()
    {
        var (policy, service) = await BuildAsync();
        Assert.That((await service.AuthorizeAsync(User(new Claim("app_permission", "backups:read")), null, policy)).Succeeded, Is.True);
        Assert.That((await service.AuthorizeAsync(User(new Claim(ClaimTypes.Role, UserRole.Administrator)), null, policy)).Succeeded, Is.False);
    }
}
