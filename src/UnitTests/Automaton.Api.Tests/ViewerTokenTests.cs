using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ViewerTokenTests
{
    private static ViewerTokenService Tokens(string name = "viewer") => new(DataProtectionProvider.Create(name + Guid.NewGuid().ToString("N")));

    [Test]
    public void Token_IsBoundToItsAppAndExpires()
    {
        var tokens = Tokens();
        var app = Guid.NewGuid();
        var user = Guid.NewGuid();
        Assert.That(tokens.Validate(tokens.Create(app, user), app), Is.EqualTo(user));
        Assert.That(tokens.Validate(tokens.Create(app, user), Guid.NewGuid()), Is.Null);
        Assert.That(tokens.Validate(tokens.Create(app, user, TimeSpan.FromSeconds(-1)), app), Is.Null);
        Assert.That(tokens.Validate("forged", app), Is.Null);
        Assert.That(tokens.Validate(null, app), Is.Null);
        Assert.That(Tokens().Validate(tokens.Create(app, user), app), Is.Null);
    }

    [Test]
    public async Task ProxyAnswer_ReportsRoleAndToken_ForAdministratorAndOrdinaryUser()
    {
        var appId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        await InternalRuntimeControllerTests.SeedRestrictedAppAsync(db, appId, owner);
        var roleId = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = roleId, Name = UserRole.SystemAdministrator, NormalizedName = UserRole.SystemAdministrator.ToUpperInvariant() });
        db.UserRoles.Add(new ApplicationUserRole { UserId = admin, RoleId = roleId });
        db.AppShares.Add(new AppShare { WebAppId = appId, UserId = other });
        await db.SaveChangesAsync();

        async Task<DefaultHttpContext> Ask(Guid user)
        {
            var request = InternalRuntimeControllerTests.CreateAuthorizedRequest(appId, user);
            var controller = InternalRuntimeControllerTests.CreateController(db, request.HttpContext, request.CookieService);
            // a browser-supplied header must not influence the answer
            request.HttpContext.Request.Headers[ViewerHeaders.Role] = "administrator";
            var result = await controller.AuthorizeRuntimeRequest(
                request.HttpContext.Request.Headers[RuntimeCookieConstants.AppIdHeader].ToArray(),
                request.HttpContext.Request.Headers[RuntimeCookieConstants.ProxyAuthHeader].ToArray(), CancellationToken.None);
            Assert.That(result, Is.InstanceOf<NoContentResult>());
            return request.HttpContext;
        }

        Assert.That((await Ask(admin)).Response.Headers[ViewerHeaders.Role].ToString(), Is.EqualTo("administrator"));
        var ordinary = await Ask(other);
        Assert.That(ordinary.Response.Headers[ViewerHeaders.Role].ToString(), Is.EqualTo("user"));
        Assert.That(ordinary.Response.Headers[ViewerHeaders.Token].ToString(), Is.Not.Empty);
    }

    [Test]
    public async Task CleanupChanges_NeedAnAdministratorViewerToken_WhenTheCallerIsAnApp()
    {
        var appId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var other = Guid.NewGuid();
        var tokens = Tokens();
        var db = AppAuthorizationTestData.CreateDbContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = roleId, Name = UserRole.SystemAdministrator, NormalizedName = UserRole.SystemAdministrator.ToUpperInvariant() });
        db.UserRoles.Add(new ApplicationUserRole { UserId = admin, RoleId = roleId });
        await db.SaveChangesAsync();
        var authorization = new ResourceAuthorizationService(db, new UserContextService(new HttpContextAccessor()));
        var service = new ServerCleanupService(db, new FakeRunner());

        async Task<int?> Put(string? token, bool app = true, Guid? tokenApp = null)
        {
            var http = new DefaultHttpContext();
            if (app) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", appId.ToString())], "AppPermission"));
            if (token is not null) http.Request.Headers[ViewerHeaders.Token] = token;
            var controller = new ServerCleanupController(service, tokens, authorization) { ControllerContext = new ControllerContext { HttpContext = http } };
            var result = await controller.Update(new ServerCleanupSettingsDto("05:00", 7, 200), default);
            return (result.Result as StatusCodeResult)?.StatusCode;
        }

        Assert.That(await Put(null), Is.EqualTo(403));
        Assert.That(await Put("forged"), Is.EqualTo(403));
        Assert.That(await Put(tokens.Create(appId, other)), Is.EqualTo(403));
        Assert.That(await Put(tokens.Create(appId, admin, TimeSpan.FromSeconds(-1))), Is.EqualTo(403));
        Assert.That(await Put(tokens.Create(Guid.NewGuid(), admin)), Is.EqualTo(403));
        Assert.That(await Put(tokens.Create(appId, admin)), Is.Null.Or.Not.EqualTo(403));
        Assert.That(await Put(null, app: false), Is.Null.Or.Not.EqualTo(403));
    }
}
