using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using static Automaton.Api.Tests.AppMarketplaceListingTestData;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMarketplacePermissionTests
{
    [Test]
    public async Task ImageChanges_RequireUpdateAccessNotJustVisibility()
    {
        using var db = CreateSeededDb();
        var viewer = CreateImageService(db, ViewerId);

        var viewerResults = new[]
        {
            await viewer.UploadAsync(AppId, null, "viewer"),
            await viewer.UploadScreenshotAsync(AppId, null, "viewer"),
            await viewer.DeleteScreenshotAsync(AppId, Guid.NewGuid(), "viewer")
        };
        // A Developer passes the permission check and reaches file validation.
        var developer = await CreateImageService(db, DeveloperId).UploadScreenshotAsync(AppId, null, "developer");

        Assert.Multiple(() =>
        {
            Assert.That(viewerResults.Select(result => result.StatusCode), Has.All.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(developer.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        });
    }

    [Test]
    public async Task ListingEndpoint_RefusesReadOnlyKeysWithAnExplanation()
    {
        var controller = new AppMarketplaceListingController(null!) { ControllerContext = ReadOnlyKeyContext() };

        var result = await controller.Update(AppId, new UpdateAppListingRequest(Summary: "x"), CancellationToken.None);

        AssertKeyScopeDenied(result);
    }

    [Test]
    public async Task ImageEndpoints_RefuseReadOnlyKeysWithAnExplanation()
    {
        var controller = new AppsController(null!, NullLogger<AppsController>.Instance, null!, null!, null!, null!)
        {
            ControllerContext = ReadOnlyKeyContext()
        };

        AssertKeyScopeDenied(await controller.UploadBackgroundImage(AppId, null!));
        AssertKeyScopeDenied(await controller.UploadScreenshot(AppId, null!));
        AssertKeyScopeDenied(await controller.DeleteScreenshot(AppId, Guid.NewGuid()));
    }

    [TestCase(null, true)]
    [TestCase("Developer", true)]
    [TestCase("FullAccess", true)]
    [TestCase("ReadOnly", false)]
    [TestCase("Runner", false)]
    public void AppChangeKeyScope_AllowsSignedInUsersAndWritableKeys(string? scope, bool allowed)
    {
        var claims = scope is null ? Array.Empty<Claim>() : [new Claim("api_key_scope", scope)];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        Assert.That(AppChangeKeyScope.IsAllowed(user), Is.EqualTo(allowed));
    }

    private static void AssertKeyScopeDenied(IActionResult result)
    {
        var objectResult = result as ObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(objectResult?.Value?.ToString(), Does.Contain(AppChangeKeyScope.DeniedMessage));
        });
    }

    private static ControllerContext ReadOnlyKeyContext() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("api_key_scope", "ReadOnly")], "test"))
        }
    };

    private static AppImageManagementService CreateImageService(ApplicationDbContext db, Guid userId)
    {
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        return new AppImageManagementService(
            AppAuthorizationTestServiceFactory.CreateAppsService(db, userId),
            new ResourceAuthorizationService(db, userContext),
            new AppAuthorizationTestWebHostEnvironment(Path.GetTempPath()),
            NullLogger<AppImageManagementService>.Instance);
    }
}
