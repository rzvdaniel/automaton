using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Controllers;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Extensions;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PermissionsAdminKeyAccessTests
{
    private static ClaimsPrincipal Principal(string? scope, params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        if (scope is not null) claims.Add(new Claim("api_key_scope", scope));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<bool> PolicyAllowsAsync(string policyName, ClaimsPrincipal user)
    {
        var services = new ServiceCollection().AddLogging().AddApiAuthorization().BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(policyName);
        return (await authorization.AuthorizeAsync(user, null, policy!.Requirements)).Succeeded;
    }

    [TestCase(null, "SysAdmin", true, TestName = "administrator login")]
    [TestCase("PermissionsAdmin", "SysAdmin", true, TestName = "permissions-admin key of an administrator")]
    [TestCase("Developer", "SysAdmin", false, TestName = "ordinary CLI key of an administrator")]
    [TestCase("FullAccess", "SysAdmin", false, TestName = "full-access key of an administrator")]
    [TestCase("ReadOnly", "SysAdmin", false, TestName = "read-only key of an administrator")]
    [TestCase("PermissionsAdmin", "User", false, TestName = "permissions-admin key without the role")]
    [TestCase(null, "Admin", false, TestName = "plain administrator login")]
    [TestCase(null, "User", false, TestName = "ordinary login")]
    public async Task Policy_DecidesByRoleAndKeyScope(string? scope, string role, bool allowed)
        => Assert.That(await PolicyAllowsAsync(PermissionsAdminKey.PolicyName, Principal(scope, role)), Is.EqualTo(allowed));

    [TestCase("PermissionsAdmin", true, 403, TestName = "CLI key approving root access is refused")]
    [TestCase("PermissionsAdmin", false, 404, TestName = "CLI key rejecting root access still reaches the service")]
    [TestCase(null, true, 404, TestName = "Studio session approving root access reaches the service")]
    public async Task HostExecApprovalIsRefusedForAKey(string? scope, bool approved, int expected)
    {
        var controller = new AdminAppRunnerPermissionsController(null!, null!, null!)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = Principal(scope, "SysAdmin") } }
        };
        if (expected == 403)
        {
            var result = await controller.Set(Guid.NewGuid(), Guid.NewGuid(), new(approved), "runner:host-exec", default);
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(403));
        }
        else Assert.ThrowsAsync<NullReferenceException>(() => controller.Set(Guid.NewGuid(), Guid.NewGuid(), new(approved), "runner:host-exec", default),
            "the guard let the call through to the service (null here)");
    }

    [Test]
    public void TheDefaultPolicyStillExcludesApiKeys_SoOtherAdminEndpointsStay401ForEveryKey()
    {
        var services = new ServiceCollection().AddLogging().AddApiAuthorization().BuildServiceProvider();
        var schemes = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value.DefaultPolicy.AuthenticationSchemes;
        Assert.That(schemes, Does.Not.Contain(ApiKeyAuthenticationSchemeOptions.DefaultScheme));
    }

    [TestCase(typeof(AdminAppPermissionsController))]
    [TestCase(typeof(AdminAppRunnerPermissionsController))]
    [TestCase(typeof(AdminPendingPermissionsController))]
    public void OnlyTheApprovalControllersUseThePolicy(Type controller)
    {
        var attributes = controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().ToList();
        Assert.That(attributes.Select(attribute => attribute.Policy), Is.EqualTo(new[] { PermissionsAdminKey.PolicyName }));
    }

    [Test]
    public void NoOtherAdminControllerAcceptsTheApiKeyScheme()
    {
        var others = typeof(AdminAppsController).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AdminAppsController).Namespace && typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => type != typeof(AdminAppPermissionsController) && type != typeof(AdminAppRunnerPermissionsController)
                && type != typeof(AdminPendingPermissionsController));
        foreach (var type in others)
        {
            var attributes = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().ToList();
            Assert.That(attributes.Select(attribute => attribute.Policy), Does.Not.Contain(PermissionsAdminKey.PolicyName), type.Name);
        }
    }

    private static ApiKeysController ControllerFor(ClaimsPrincipal user) => new(null!, null!, null!, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
    };

    [Test]
    public async Task RegularUserCannotCreateAnAdminScopedKey()
    {
        var result = await ControllerFor(Principal(null, "User")).CreateApiKey(new CreateApiKeyRequest
        { Name = "x", Scope = ApiKeyScope.PermissionsAdmin, ExpiresAt = DateTime.UtcNow.AddDays(1) });

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task PlainAdministratorCannotCreateAnAdminScopedKey()
    {
        var result = await ControllerFor(Principal(null, "Admin")).CreateApiKey(new CreateApiKeyRequest
        { Name = "x", Scope = ApiKeyScope.PermissionsAdmin, ExpiresAt = DateTime.UtcNow.AddDays(1) });

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task AKeyCannotCreateAnAdminScopedKey()
    {
        var result = await ControllerFor(Principal("PermissionsAdmin", "SysAdmin")).CreateApiKey(new CreateApiKeyRequest
        { Name = "x", Scope = ApiKeyScope.PermissionsAdmin, ExpiresAt = DateTime.UtcNow.AddDays(1) });

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(403));
    }

    [TestCase(null)]
    [TestCase(-1)]
    [TestCase(91)]
    public async Task AdminScopedKeyNeedsAnExpiryWithinNinetyDays(int? days)
    {
        var result = await ControllerFor(Principal(null, "SysAdmin")).CreateApiKey(new CreateApiKeyRequest
        { Name = "x", Scope = ApiKeyScope.PermissionsAdmin, ExpiresAt = days is null ? null : DateTime.UtcNow.AddDays(days.Value) });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }
}
