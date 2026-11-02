using System.Security.Claims;
using Automaton.Api.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PasswordChangeRequirementTests
{
    [TestCase("/api/ApiKeys", true)]
    [TestCase("/api/apps", true)]
    [TestCase("/api/cli/authorize", true)]
    [TestCase("/api/Account/UpdateUserProfile", true)]
    [TestCase("/api/Account/UpdateUserPassword", false)]
    [TestCase("/api/account/updateuserpassword", false)]
    [TestCase("/api/User/GetUserProfile", false)]
    [TestCase("/api/user-settings", false)]
    [TestCase("/api/Token/Logout", false)]
    [TestCase("/api/Token/RefreshAccessToken", false)]
    [TestCase("/api/TokenThief", true)]
    [TestCase("/healthz", false)]
    [TestCase("/_Host", true)]
    public void OnlyThePasswordChangePageIsReachable(string path, bool blocked)
        => Assert.That(PasswordChangeRequirement.IsBlocked(new PathString(path)), Is.EqualTo(blocked));

    [Test]
    public async Task FlaggedUserGets403WithACodeAndOthersPassThrough()
    {
        var reached = 0;
        var middleware = new PasswordChangeRequirement(_ => { reached++; return Task.CompletedTask; });
        using var db = AppAuthorizationTestData.CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var flagged = Context("/api/ApiKeys", new Claim(PasswordChangeRequirement.ClaimType, "true"));
        await middleware.InvokeAsync(flagged, db, cache);
        flagged.Response.Body.Position = 0;
        var body = await new StreamReader(flagged.Response.Body).ReadToEndAsync();

        await middleware.InvokeAsync(Context("/api/ApiKeys"), db, cache);
        await middleware.InvokeAsync(Context("/api/Account/UpdateUserPassword", new Claim(PasswordChangeRequirement.ClaimType, "true")), db, cache);

        Assert.Multiple(() =>
        {
            Assert.That(flagged.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(body, Does.Contain(PasswordChangeRequirement.ErrorCode));
            Assert.That(reached, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task APrincipalWithoutTheClaimIsCheckedAgainstTheDatabase()
    {
        // An Identity cookie (GitHub sign-in) or an API key carries no claim; the stored flag still applies.
        var reached = 0;
        var middleware = new PasswordChangeRequirement(_ => { reached++; return Task.CompletedTask; });
        using var db = AppAuthorizationTestData.CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        db.Users.Add(new Automaton.Api.Entities.ApplicationUser
        {
            Id = userId, PublicUsername = "flagged", UserName = "flagged@example.com", Email = "flagged@example.com", MustChangePassword = true
        });
        await db.SaveChangesAsync();
        var cookie = Context("/api/ApiKeys", new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        await middleware.InvokeAsync(cookie, db, cache);
        Assert.That(cookie.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));

        (await db.Users.SingleAsync(user => user.Id == userId)).MustChangePassword = false;
        await db.SaveChangesAsync();
        await middleware.InvokeAsync(Context("/api/ApiKeys", new Claim(ClaimTypes.NameIdentifier, userId.ToString())), db, cache);
        Assert.That(reached, Is.EqualTo(1), "a cleared flag takes effect at once");
    }

    private static DefaultHttpContext Context(string path, params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
