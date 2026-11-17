using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Home;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Studio Home: Needs-you cards per role, pins and dismissals that belong to one person.</summary>
[TestFixture]
public sealed class HomeTests
{
    private sealed class StubNeedsYou : HomeNeedsYouService
    {
        public StubNeedsYou() : base(null!, null!, null!, null!, null!, null!) { }
        public override Task<List<HomeCard>> ServerCardsAsync(CancellationToken token)
            => Task.FromResult(new List<HomeCard>
            {
                new(HomeCardKeys.NoSmtp, HomeCardKeys.NoSmtp, "info", "Set up email", "x", "Open", "/settings", true),
                new(HomeCardKeys.NoRunner, HomeCardKeys.NoRunner, "warn", "Add a Runner", "x", "Open", "/server", false),
            });
    }

    private static (ApplicationDbContext Db, HomeController Controller) Create(Guid userId, ApplicationDbContext? db = null)
    {
        db ??= AppAuthorizationTestData.CreateDbContext();
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(userId));
        var controller = new HomeController(new HomeService(db, authorization, new StubNeedsYou()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(userId == Guid.Empty ? [] : [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")) } }
        };
        return (db, controller);
    }

    private static void MakeSystemAdministrator(ApplicationDbContext db, Guid userId)
    {
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = UserRole.SystemAdministrator, NormalizedName = "SYSADMIN" };
        db.Roles.Add(role);
        db.UserRoles.Add(new ApplicationUserRole { UserId = userId, RoleId = role.Id });
        db.SaveChanges();
    }

    [Test]
    public async Task AdministratorSeesServerCardsAndAUserDoesNot()
    {
        Guid admin = Guid.NewGuid(), user = Guid.NewGuid();
        var (db, adminHome) = Create(admin);
        MakeSystemAdministrator(db, admin);
        var (_, userHome) = Create(user, db);

        var adminCards = ((HomeResponse)((OkObjectResult)(await adminHome.Get(default)).Result!).Value!).NeedsYou;
        var userCards = ((HomeResponse)((OkObjectResult)(await userHome.Get(default)).Result!).Value!).NeedsYou;

        Assert.That(adminCards.Select(card => card.Key), Is.EquivalentTo(new[] { HomeCardKeys.NoSmtp, HomeCardKeys.NoRunner }));
        Assert.That(userCards, Is.Empty, "server cards are for system administrators only");
    }

    [Test]
    public async Task DismissingAnOptionalCardHidesItForThatPersonOnly()
    {
        Guid admin = Guid.NewGuid(), other = Guid.NewGuid();
        var (db, adminHome) = Create(admin);
        MakeSystemAdministrator(db, admin);
        MakeSystemAdministrator(db, other);
        var (_, otherHome) = Create(other, db);
        (_, adminHome) = Create(admin, db);

        Assert.That(await adminHome.Dismiss(HomeCardKeys.NoSmtp, default), Is.TypeOf<NoContentResult>());
        Assert.That(await adminHome.Dismiss(HomeCardKeys.NoSmtp, default), Is.TypeOf<NoContentResult>(), "again is fine");
        Assert.That(await adminHome.Dismiss(HomeCardKeys.NoRunner, default), Is.TypeOf<NotFoundResult>(), "required cards cannot be dismissed");
        Assert.That(await adminHome.Dismiss("nonsense", default), Is.TypeOf<NotFoundResult>());

        Assert.That(Keys(await adminHome.Get(default)), Is.EqualTo(new[] { HomeCardKeys.NoRunner }));
        (_, otherHome) = Create(other, db);
        Assert.That(Keys(await otherHome.Get(default)), Has.Member(HomeCardKeys.NoSmtp));
    }

    [Test]
    public async Task PinsListInOrderAndOnlyForAppsOnThePersonsList()
    {
        Guid owner = Guid.NewGuid(), stranger = Guid.NewGuid(), first = Guid.NewGuid(), second = Guid.NewGuid(), foreign = Guid.NewGuid();
        var (db, home) = Create(owner);
        AppAuthorizationTestData.SeedApp(db, first, owner, "first");
        AppAuthorizationTestData.SeedApp(db, second, owner, "second");
        AppAuthorizationTestData.SeedApp(db, foreign, Guid.NewGuid(), "foreign");

        Assert.That(await home.Pin(first, default), Is.TypeOf<NoContentResult>());
        Assert.That(await home.Pin(second, default), Is.TypeOf<NoContentResult>());
        Assert.That(await home.Pin(second, default), Is.TypeOf<NoContentResult>(), "pinning twice is fine");
        Assert.That(await home.Pin(foreign, default), Is.TypeOf<NotFoundResult>(), "someone else's app");
        Assert.That(await home.Pin(Guid.NewGuid(), default), Is.TypeOf<NotFoundResult>(), "unknown app");
        Assert.That(db.UserHomePins.Count(), Is.EqualTo(2));
        Assert.That(PinnedIds(await home.Get(default)), Is.EqualTo(new[] { first, second }));

        Assert.That(await home.Reorder(new HomePinOrderRequest([second, first]), default), Is.TypeOf<NoContentResult>());
        Assert.That(PinnedIds(await home.Get(default)), Is.EqualTo(new[] { second, first }));
        Assert.That(await home.Reorder(new HomePinOrderRequest([second]), default), Is.TypeOf<BadRequestObjectResult>(), "must name every pin");
        Assert.That(await home.Reorder(new HomePinOrderRequest([second, foreign]), default), Is.TypeOf<BadRequestObjectResult>());

        var (_, strangerHome) = Create(stranger, db);
        Assert.That(PinnedIds(await strangerHome.Get(default)), Is.Empty, "pins are private");
        Assert.That(await strangerHome.Unpin(first, default), Is.TypeOf<NoContentResult>());
        Assert.That(db.UserHomePins.Count(), Is.EqualTo(2), "someone else's unpin changes nothing");

        // The signed-in user comes from an ambient accessor, so sign the owner in again.
        (_, home) = Create(owner, db);
        Assert.That(await home.Unpin(first, default), Is.TypeOf<NoContentResult>());
        Assert.That(PinnedIds(await home.Get(default)), Is.EqualTo(new[] { second }));
    }

    [Test]
    public void FailedAppsBecomeCardsForPeopleWhoCanChangeThem()
    {
        var failing = new AppListItemDto { Id = Guid.NewGuid(), Name = "Shop", CanUpdate = true, LiveEnvironment = new AppListEnvironmentDto { Status = "Failed" } };
        var running = new AppListItemDto { Id = Guid.NewGuid(), Name = "Blog", CanUpdate = true, LiveEnvironment = new AppListEnvironmentDto { Status = "Running" } };
        var notMine = new AppListItemDto { Id = Guid.NewGuid(), Name = "Wiki", CanUpdate = false, LiveEnvironment = new AppListEnvironmentDto { Status = "Failed" } };
        var paused = new AppListItemDto { Id = Guid.NewGuid(), Name = "Old", CanUpdate = true, IsPaused = true, LiveEnvironment = new AppListEnvironmentDto { Status = "Failed" } };
        var action = new AppListItemDto { Id = Guid.NewGuid(), Name = "Nightly", CanUpdate = true, ActionsOnly = true, LastActionRun = new AppListActionRunDto { Status = "Failed" } };

        var cards = HomeNeedsYouService.AppCards([failing, running, notMine, paused, action]).ToList();

        Assert.That(cards.Select(card => card.AppId), Is.EqualTo(new Guid?[] { failing.Id, action.Id }));
        Assert.That(cards.All(card => !card.Dismissible && card.ActionPath.StartsWith("/apps/", StringComparison.Ordinal)));
    }

    [Test]
    public void HomeIsForSignedInPeopleAndNotForScopedKeys()
    {
        var policy = typeof(HomeController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo("DeveloperApiAccess"));
        Assert.That(typeof(HomeController).GetMethods().Where(m => m.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0), Is.Empty);
    }

    [Test]
    public async Task AnAnonymousCallerIsUnauthorized()
    {
        var (_, home) = Create(Guid.Empty);
        Assert.That((await home.Get(default)).Result, Is.TypeOf<UnauthorizedResult>());
        Assert.That(await home.Pin(Guid.NewGuid(), default), Is.TypeOf<UnauthorizedResult>());
    }

    private static string[] Keys(ActionResult<HomeResponse> result)
        => ((HomeResponse)((OkObjectResult)result.Result!).Value!).NeedsYou.Select(card => card.Key).ToArray();

    private static Guid[] PinnedIds(ActionResult<HomeResponse> result)
        => ((HomeResponse)((OkObjectResult)result.Result!).Value!).Pinned.Select(pin => pin.App.Id).ToArray();
}
