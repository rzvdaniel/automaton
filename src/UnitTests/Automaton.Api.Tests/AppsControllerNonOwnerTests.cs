using Automaton.Api.Controllers;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using static Automaton.Api.Tests.AppMarketplaceListingTestData;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppsControllerNonOwnerTests
{
    private static readonly Guid StrangerId = Guid.NewGuid();

    [TestCase("stranger")]
    [TestCase("viewer")]
    public async Task UpdatePublishUnpublishAndRunnerChange_AnswerForbiddenInsteadOfFailing(string caller)
    {
        using var db = CreateSeededDb();
        var userId = caller == "viewer" ? ViewerId : StrangerId;
        var controller = new AppsController(
            AppAuthorizationTestServiceFactory.CreateAppsService(db, userId),
            NullLogger<AppsController>.Instance,
            AppAuthorizationTestData.CreateUserContextService(userId),
            new ConfirmedGuard(),
            null!,
            null!);

        var results = new[]
        {
            await controller.Put(AppId, new UpdateAppRequest { Name = "x", Summary = "x", Description = "x" }),
            await controller.Publish(AppId, new PublishAppRequest { PublicUrl = "e2e-hijack" }, CancellationToken.None),
            await controller.Unpublish(AppId, CancellationToken.None),
            await controller.UpdateRunner(AppId, new Automaton.Api.Controllers.UpdateRunnerRequest())
        };

        Assert.That(results.Select(result => (result as ObjectResult)?.StatusCode ?? (result as StatusCodeResult)?.StatusCode),
            Has.All.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task ADeveloper_StillPassesThePermissionCheck()
    {
        using var db = CreateSeededDb();
        var controller = new AppsController(
            AppAuthorizationTestServiceFactory.CreateAppsService(db, DeveloperId),
            NullLogger<AppsController>.Instance,
            AppAuthorizationTestData.CreateUserContextService(DeveloperId),
            new ConfirmedGuard(),
            null!,
            null!);

        var result = await controller.UpdateRunner(AppId, new Automaton.Api.Controllers.UpdateRunnerRequest());

        Assert.That((result as ObjectResult)?.StatusCode, Is.Not.EqualTo(StatusCodes.Status403Forbidden));
    }

    private sealed class ConfirmedGuard : ConfirmedEmailGuardService
    {
        public ConfirmedGuard() : base(null!) { }
        public override Task<bool> IsConfirmedAsync(Guid userId) => Task.FromResult(true);
    }
}
