using Automaton.Api.Controllers;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Who may read an app's permission status, and who may list apps for approval.</summary>
[TestFixture]
public sealed class AppPermissionsControllerTests
{
    [Test]
    public async Task OnlyPeopleWhoCanChangeTheAppSeeItsPermissionStatus()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid appId = Guid.NewGuid(), projectId = Guid.NewGuid(), owner = Guid.NewGuid(), developer = Guid.NewGuid();
        AppAuthorizationTestData.SeedProjectScopedApp(db, appId, projectId, owner, developer);
        db.ProjectMembers.Add(new Automaton.Api.Entities.ProjectMember
        {
            ProjectId = projectId, UserId = owner, Role = Automaton.Api.Enums.ProjectRole.ProjectAdmin, JoinedAt = DateTime.UtcNow, IsActive = true
        });
        await db.SaveChangesAsync();

        Assert.That(await Controller(db, owner).Get(appId, default), Is.TypeOf<OkObjectResult>());
        Assert.That(await Controller(db, developer).Get(appId, default), Is.TypeOf<OkObjectResult>());
        Assert.That(await Controller(db, Guid.NewGuid()).Get(appId, default), Is.TypeOf<ForbidResult>(), "a stranger");
        Assert.That(await Controller(db, owner).Get(Guid.NewGuid(), default), Is.TypeOf<ForbidResult>(), "an unknown app reveals nothing");
    }

    [Test]
    public void StatusAcceptsOwnerKeysButNotTheAppsOwnCredential()
    {
        var policy = Policy(typeof(AppPermissionsController));
        // AllowApiKey takes owner API keys and logins; the app-permission scheme (the running app's own credential) is not in it.
        Assert.That(policy.Policy, Is.EqualTo("AllowApiKey"));
        Assert.That(policy.AuthenticationSchemes, Is.Null.Or.Empty);
    }

    [Test]
    public void TheAppDirectoryIsForAdministratorsSignedInToStudio()
    {
        var policy = Policy(typeof(AdminAppsController));
        Assert.That(policy.Policy, Is.EqualTo("Administrators"));
        Assert.That(policy.AuthenticationSchemes, Is.Null.Or.Empty, "no API key scheme");
        Assert.That(typeof(AdminAppsController).GetMethods()
            .Where(method => method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0), Is.Empty);
    }

    private static AuthorizeAttribute Policy(Type controller) =>
        controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

    private static AppPermissionsController Controller(Automaton.Api.Data.ApplicationDbContext db, Guid userId)
    {
        var users = AppAuthorizationTestData.CreateUserContextService(userId);
        var configuration = new ConfigurationService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:WebsiteUrl"] = "https://studio.example.com" })
            .Build());
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        return new AppPermissionsController(new AppPermissionService(db, protector),
            new ResourceAuthorizationService(db, users), users, configuration);
    }
}
