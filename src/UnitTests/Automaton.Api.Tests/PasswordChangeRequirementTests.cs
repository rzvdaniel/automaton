using System.Security.Claims;
using Automaton.Api.Authentication;
using Microsoft.AspNetCore.Http;
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
    public void OnlyThePasswordChangePageIsReachable(string path, bool blocked)
        => Assert.That(PasswordChangeRequirement.IsBlocked(new PathString(path)), Is.EqualTo(blocked));

    [Test]
    public async Task FlaggedUserGets403WithACodeAndOthersPassThrough()
    {
        var reached = 0;
        var middleware = new PasswordChangeRequirement(_ => { reached++; return Task.CompletedTask; });

        var flagged = Context("/api/ApiKeys", new Claim(PasswordChangeRequirement.ClaimType, "true"));
        await middleware.InvokeAsync(flagged);
        flagged.Response.Body.Position = 0;
        var body = await new StreamReader(flagged.Response.Body).ReadToEndAsync();

        await middleware.InvokeAsync(Context("/api/ApiKeys"));
        await middleware.InvokeAsync(Context("/api/Account/UpdateUserPassword", new Claim(PasswordChangeRequirement.ClaimType, "true")));

        Assert.Multiple(() =>
        {
            Assert.That(flagged.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(body, Does.Contain(PasswordChangeRequirement.ErrorCode));
            Assert.That(reached, Is.EqualTo(2));
        });
    }

    private static DefaultHttpContext Context(string path, params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
