using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppSharingTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AppId = Guid.NewGuid();

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        AppAuthorizationTestData.SeedApp(db, AppId, OwnerId, "my-app");
        return db;
    }

    private static HttpContext Http(Guid userId) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
    };

    private static AppSharingService Service(ApplicationDbContext db, Guid userId) =>
        UserAppsTestServices.CreateSharingService(db, Http(userId));

    private static Task<bool> CanOpenAsync(ApplicationDbContext db, Guid userId)
    {
        // HttpContextAccessor keeps its context in shared async-local state: do not assign one here.
        var context = new UserContextService(new HttpContextAccessor());
        return new ResourceAuthorizationService(db, context).CanUseAppRuntimeAsync(AppId, userId);
    }

    private static ApplicationUser AddUser(ApplicationDbContext db, string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email, PublicUsername = email
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    [Test]
    public async Task NewApp_IsOnlyMe_AndStrangersCannotOpenIt()
    {
        await using var db = CreateDb();
        var stranger = Guid.NewGuid();

        var state = await Service(db, OwnerId).GetAsync(AppId, default);

        Assert.That(state.Audience, Is.EqualTo("me"));
        Assert.That(await CanOpenAsync(db, OwnerId), Is.True);
        Assert.That(await CanOpenAsync(db, stranger), Is.False);
    }

    [Test]
    public async Task SharingWithAPerson_LetsOnlyThatPersonOpen_AndShowsPeople()
    {
        await using var db = CreateDb();
        var friend = AddUser(db, "friend@example.test");
        var stranger = AddUser(db, "stranger@example.test");

        var state = await Service(db, OwnerId).AddPersonAsync(AppId, " Friend@Example.test ", default);

        Assert.That(state.Audience, Is.EqualTo("people"));
        Assert.That(state.People.Single().Email, Is.EqualTo("friend@example.test"));
        Assert.That(await CanOpenAsync(db, friend.Id), Is.True);
        Assert.That(await CanOpenAsync(db, stranger.Id), Is.False);
    }

    [Test]
    public async Task RemovingAPerson_ClosesTheAppToThemAgain()
    {
        await using var db = CreateDb();
        var friend = AddUser(db, "friend@example.test");
        var service = Service(db, OwnerId);
        await service.AddPersonAsync(AppId, friend.Email!, default);

        var state = await service.RemovePersonAsync(AppId, friend.Id, default);

        Assert.That(state.Audience, Is.EqualTo("me"));
        Assert.That(await CanOpenAsync(db, friend.Id), Is.False);
    }

    [Test]
    public async Task Everyone_OnThisServer_OpensIt_AndOnlyMeClosesItAndForgetsPeople()
    {
        await using var db = CreateDb();
        var friend = AddUser(db, "friend@example.test");
        var other = AddUser(db, "other@example.test");
        var service = Service(db, OwnerId);
        await service.AddPersonAsync(AppId, friend.Email!, default);

        var server = await service.SetAudienceAsync(AppId, "server", null, null, null, default);
        Assert.That(server.Audience, Is.EqualTo("server"));
        Assert.That(await CanOpenAsync(db, other.Id), Is.True);

        var onlyMe = await service.SetAudienceAsync(AppId, "me", null, null, null, default);
        Assert.That(onlyMe.Audience, Is.EqualTo("me"));
        Assert.That(onlyMe.People, Is.Empty);
        Assert.That(await CanOpenAsync(db, other.Id), Is.False);
        Assert.That(await CanOpenAsync(db, friend.Id), Is.False);
    }

    [Test]
    public async Task Internet_WithoutAPublicAddress_IsRejected()
    {
        await using var db = CreateDb();
        var app = await db.WebApps.SingleAsync();
        app.PublicUrl = null;
        await db.SaveChangesAsync();

        var error = Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db, OwnerId).SetAudienceAsync(AppId, "internet", null, null, null, default));

        Assert.That(error!.Message, Does.Contain("public address"));
    }

    [Test]
    public async Task UnknownChoice_IsRejected()
    {
        await using var db = CreateDb();

        Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db, OwnerId).SetAudienceAsync(AppId, "everyone-ever", null, null, null, default));
    }

    [Test]
    public async Task UnknownEmail_Is404_AndYourOwnEmail_IsRejected()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner@example.test");
        db.WebAppUsers.Add(new WebAppUser { WebAppId = AppId, UserId = owner.Id });
        await db.SaveChangesAsync();
        var service = Service(db, owner.Id);

        Assert.ThrowsAsync<KeyNotFoundException>(() => service.AddPersonAsync(AppId, "nobody@example.test", default));
        Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPersonAsync(AppId, "owner@example.test", default));
    }

    [Test]
    public async Task SharedPerson_CannotChangeSharing_AndMissingAppIs404()
    {
        await using var db = CreateDb();
        var friend = AddUser(db, "friend@example.test");
        await Service(db, OwnerId).AddPersonAsync(AppId, friend.Email!, default);
        var asFriend = Service(db, friend.Id);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => asFriend.SetAudienceAsync(AppId, "server", null, null, null, default));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => asFriend.GetAsync(AppId, default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => Service(db, OwnerId).GetAsync(Guid.NewGuid(), default));
    }

    [Test]
    public async Task SharedAndServerWideApps_AppearInThePersonsAppsList()
    {
        await using var db = CreateDb();
        var friend = AddUser(db, "friend@example.test");
        var context = new UserContextService(new HttpContextAccessor());
        await Service(db, OwnerId).AddPersonAsync(AppId, friend.Email!, default);

        var organizations = Organizations(db, context);
        var listed = await organizations.GetMyAssignedAppsAsync(friend.Id);

        Assert.That(listed.Select(app => app.Id), Is.EqualTo(new[] { AppId }));
    }

    private static Project AddTeam(ApplicationDbContext db, params ApplicationUser[] members)
    {
        var team = new Project { Id = Guid.NewGuid(), Name = "design", DisplayName = "Design", IsActive = true };
        db.Projects.Add(team);
        foreach (var member in members)
            db.ProjectMembers.Add(new ProjectMember { ProjectId = team.Id, UserId = member.Id, IsActive = true });
        db.SaveChanges();
        return team;
    }

    [Test]
    public async Task Team_LetsItsMembersOpen_AndNonMembersGet403Equivalent()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner@example.test");
        var member = AddUser(db, "member@example.test");
        var outsider = AddUser(db, "outsider@example.test");
        var team = AddTeam(db, owner, member);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = AppId, UserId = owner.Id });
        await db.SaveChangesAsync();

        var state = await Service(db, owner.Id).SetAudienceAsync(AppId, "team", null, team.Id, null, default);

        Assert.That(state.Audience, Is.EqualTo("team"));
        Assert.That(state.Team, Is.EqualTo(new AppSharingTeam(team.Id, "Design")));
        Assert.That(await CanOpenAsync(db, member.Id), Is.True);
        Assert.That(await CanOpenAsync(db, outsider.Id), Is.False);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db, member.Id).SetAudienceAsync(AppId, "me", null, null, null, default));
        var listed = await Organizations(db, new UserContextService(new HttpContextAccessor())).GetMyAssignedAppsAsync(member.Id);
        Assert.That(listed.Select(app => app.Id), Is.EqualTo(new[] { AppId }));

        db.ProjectMembers.Single(m => m.UserId == member.Id).IsActive = false;
        await db.SaveChangesAsync();
        Assert.That(await CanOpenAsync(db, member.Id), Is.False);
    }

    [Test]
    public async Task Team_MustBeOneOfYourOwn_AndChangingAudienceEndsTheTeamShare()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner@example.test");
        var member = AddUser(db, "member@example.test");
        var mine = AddTeam(db, owner, member);
        var notMine = AddTeam(db, member);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = AppId, UserId = owner.Id });
        await db.SaveChangesAsync();
        var service = Service(db, owner.Id);

        Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetAudienceAsync(AppId, "team", null, notMine.Id, null, default));
        Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAudienceAsync(AppId, "team", null, null, null, default));

        await service.SetAudienceAsync(AppId, "team", null, mine.Id, null, default);
        var state = await service.SetAudienceAsync(AppId, "server", null, null, null, default);

        Assert.That(state.Team, Is.Null);
        Assert.That((await db.WebApps.SingleAsync()).SharedWithProjectId, Is.Null);
    }

    [Test]
    public async Task Internet_ListingFollowsTheSwitch_AndOtherChoicesCannotBeListed()
    {
        await using var db = CreateDb();
        var service = Service(db, OwnerId);

        var unlisted = await service.SetAudienceAsync(AppId, "internet", "my-app", null, false, default);
        Assert.That(unlisted.Audience, Is.EqualTo("internet"));
        Assert.That(unlisted.Listed, Is.False);

        var listed = await service.SetAudienceAsync(AppId, "internet", null, null, true, default);
        Assert.That(listed.Listed, Is.True);

        var kept = await service.SetAudienceAsync(AppId, "internet", null, null, null, default);
        Assert.That(kept.Listed, Is.True);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAudienceAsync(AppId, "server", null, null, true, default));
        Assert.That((await service.GetAsync(AppId, default)).Audience, Is.EqualTo("internet"), "a rejected request changes nothing");
        var closed = await service.SetAudienceAsync(AppId, "server", null, null, null, default);
        Assert.That(closed.Listed, Is.False);
    }

    [Test]
    public async Task PersonSharedEarlier_IsDeniedAfterSwitchingToATeamOrAServerOrTheInternet()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner@example.test");
        var friend = AddUser(db, "friend@example.test");
        var team = AddTeam(db, owner);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = AppId, UserId = owner.Id });
        await db.SaveChangesAsync();
        var service = Service(db, owner.Id);
        await service.AddPersonAsync(AppId, friend.Email!, default);
        Assert.That(await CanOpenAsync(db, friend.Id), Is.True);

        await service.SetAudienceAsync(AppId, "team", null, team.Id, null, default);

        Assert.That(await CanOpenAsync(db, friend.Id), Is.False);
        Assert.That((await service.GetAsync(AppId, default)).People, Is.Empty);
    }

    [Test]
    public async Task DeactivatedTeam_GrantsNoAccess_AndIsNotShownAsTheAudience()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner@example.test");
        var member = AddUser(db, "member@example.test");
        var team = AddTeam(db, owner, member);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = AppId, UserId = owner.Id });
        await db.SaveChangesAsync();
        var service = Service(db, owner.Id);
        await service.SetAudienceAsync(AppId, "team", null, team.Id, null, default);
        Assert.That(await CanOpenAsync(db, member.Id), Is.True);

        team.IsActive = false;
        await db.SaveChangesAsync();

        Assert.That(await CanOpenAsync(db, member.Id), Is.False);
        var state = await service.GetAsync(AppId, default);
        Assert.That(state.Team, Is.Null);
        Assert.That(state.Audience, Is.EqualTo("me"));
    }

    [Test]
    public async Task AddingAPersonWhileOnATeam_NarrowsToPeople_SoNonMembersStillCannotOpen()
    {
        await using var db = CreateDb();
        var owner = AddUser(db, "owner@example.test");
        var member = AddUser(db, "member@example.test");
        var bob = AddUser(db, "bob@example.test");
        var team = AddTeam(db, owner, member);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = AppId, UserId = owner.Id });
        await db.SaveChangesAsync();
        var service = Service(db, owner.Id);
        await service.SetAudienceAsync(AppId, "team", null, team.Id, null, default);

        var state = await service.AddPersonAsync(AppId, bob.Email!, default);

        Assert.That(state.Audience, Is.EqualTo("people"));
        Assert.That(state.Team, Is.Null);
        Assert.That(await CanOpenAsync(db, bob.Id), Is.True);
        Assert.That(await CanOpenAsync(db, member.Id), Is.False);
    }

    private static OrganizationService Organizations(ApplicationDbContext db, UserContextService context) =>
        new(db, context, null!, null!, null!);
}
