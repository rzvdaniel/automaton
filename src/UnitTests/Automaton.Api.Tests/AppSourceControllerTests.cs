using Automaton.Api.Controllers;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Who may read an app's stored files from the app page's Source tab.</summary>
[TestFixture]
public sealed class AppSourceControllerTests
{
    [Test]
    public async Task ManagersReadTheStoredFilesButViewOnlyMembersAndStrangersAreRefused()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid appId = Guid.NewGuid(), projectId = Guid.NewGuid(), owner = Guid.NewGuid(), developer = Guid.NewGuid(), viewer = Guid.NewGuid();
        AppAuthorizationTestData.SeedProjectScopedApp(db, appId, projectId, owner, developer);
        db.ProjectMembers.Add(new Automaton.Api.Entities.ProjectMember
        {
            ProjectId = projectId, UserId = viewer, Role = Automaton.Api.Enums.ProjectRole.Viewer, JoinedAt = DateTime.UtcNow, IsActive = true
        });
        await db.SaveChangesAsync();

        var path = Path.Combine(Path.GetTempPath(), "automaton-source-tests", Guid.NewGuid().ToString("N"));
        try
        {
            AppAuthorizationTestServiceFactory.CreateAppsService(db, developer, path, out var storage);
            var root = storage.GetAppRootPath(developer, appId);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "automaton.json"), "{}");

            Assert.That(await Controller(db, storage, developer).Files(appId, default), Is.TypeOf<OkObjectResult>());
            Assert.That(await Controller(db, storage, developer).File(appId, "automaton.json", default), Is.TypeOf<OkObjectResult>());
            Assert.That(await Controller(db, storage, viewer).Files(appId, default), Is.TypeOf<ForbidResult>(), "view-only member");
            Assert.That(await Controller(db, storage, viewer).File(appId, "automaton.json", default), Is.TypeOf<ForbidResult>());
            Assert.That(await Controller(db, storage, Guid.NewGuid()).Files(appId, default), Is.TypeOf<ForbidResult>(), "a stranger");
            Assert.That(await Controller(db, storage, Guid.NewGuid()).File(appId, "automaton.json", default), Is.TypeOf<ForbidResult>());
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private static AppSourceController Controller(Automaton.Api.Data.ApplicationDbContext db, AppFileStorageService storage, Guid userId)
    {
        var users = AppAuthorizationTestData.CreateUserContextService(userId);
        return new AppSourceController(db, new ResourceAuthorizationService(db, users), users, storage);
    }
}
