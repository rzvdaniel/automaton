using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Admin → Apps: every app with its owner, and permission requests waiting for a decision.</summary>
[TestFixture]
public sealed class AdminAppDirectoryServiceTests
{
    [Test]
    public async Task ListsEveryAppWithItsOwnerAndCountsWaitingRequests()
    {
        var (db, service) = Create(UserRole.SystemAdministrator);
        var alice = AddUser(db, "Alice Owner", "alice@example.com");
        var bob = AddUser(db, "Bob Owner", "bob@example.com");
        var asking = AddApp(db, alice, "backup-helper", [ApplicationContractValidator.SupportedPermission]);
        AddApp(db, alice, "notes", []);
        AddApp(db, bob, "shop", []);

        var all = await service.ListAsync(null, false, 1, 25, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(all.Total, Is.EqualTo(3));
            Assert.That(all.WaitingForApprovalTotal, Is.EqualTo(1));
            var item = all.Items.Single(app => app.Id == asking);
            Assert.That(item.OwnerName, Is.EqualTo("Alice Owner"));
            Assert.That(item.OwnerEmail, Is.EqualTo("alice@example.com"));
            Assert.That(item.WaitingPermissions, Is.EqualTo(new[] { ApplicationContractValidator.SupportedPermission }));
            Assert.That(all.Items.Single(app => app.Name == "shop").WaitingPermissions, Is.Empty);
        });

        var waiting = await service.ListAsync(null, true, 1, 25, CancellationToken.None);
        Assert.That(waiting.Items.Select(app => app.Id), Is.EqualTo(new[] { asking }));

        var byOwner = await service.ListAsync("bob@example", false, 1, 25, CancellationToken.None);
        Assert.That(byOwner.Items.Select(app => app.Name), Is.EqualTo(new[] { "shop" }));
    }

    [TestCase(AppPermissionStatuses.Approved)]
    [TestCase(AppPermissionStatuses.Rejected)]
    [TestCase(AppPermissionStatuses.Revoked)]
    public async Task DecidedRequestsNoLongerWait(string status)
    {
        var (db, service) = Create(UserRole.SystemAdministrator);
        var appId = AddApp(db, AddUser(db, "Owner", "owner@example.com"), "backup-helper", [ApplicationContractValidator.SupportedPermission]);
        db.AppPermissionApprovals.Add(new AppPermissionApproval
        {
            Id = Guid.NewGuid(), AppId = appId, Permission = ApplicationContractValidator.SupportedPermission, Status = status
        });
        await db.SaveChangesAsync();

        var result = await service.ListAsync(null, true, 1, 25, CancellationToken.None);
        Assert.That(result.WaitingForApprovalTotal, Is.Zero);
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public void OnlyAdministratorsCanListApps()
    {
        var (_, service) = Create(role: null);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(null, false, 1, 25, CancellationToken.None));
    }

    [Test]
    public void ApprovalLinkPointsAtTheAdminPermissionsTab()
    {
        var appId = Guid.NewGuid();
        Assert.That(AppPermissionService.ApprovalUrl("https://studio.example.com/", appId),
            Is.EqualTo($"https://studio.example.com/admin/apps/{appId}?tab=permissions"));
        Assert.That(AppPermissionService.ApprovalUrl("", appId), Is.Null);
    }

    private static (ApplicationDbContext Db, AdminAppDirectoryService Service) Create(string? role)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return (db, new AdminAppDirectoryService(db, new AdminAuthorizationService(accessor)));
    }

    private static Guid AddUser(ApplicationDbContext db, string name, string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, Name = name, DisplayName = name,
            PublicUsername = email.Split('@')[0]
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private static Guid AddApp(ApplicationDbContext db, Guid owner, string slug, List<string> permissions)
    {
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, owner, slug);
        var app = db.WebApps.Single(item => item.Id == appId);
        app.Name = slug;
        app.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract { Permissions = permissions });
        db.SaveChanges();
        return appId;
    }
}
