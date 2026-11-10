using Automaton.Api.Controllers;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Automaton.Api.Tests;

public sealed class AppListProjectAppsTests
{
    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid DeveloperId = Guid.NewGuid();
    private static readonly Guid ViewerId = Guid.NewGuid();

    private static async Task<IReadOnlyList<Automaton.Api.Models.AppListItemDto>> ListAsync(Guid userId)
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(db, AppId, ProjectId, Guid.NewGuid(), CreatorId);
        db.ProjectMembers.AddRange(
            new ProjectMember { ProjectId = ProjectId, UserId = DeveloperId, Role = ProjectRole.Developer, JoinedAt = DateTime.UtcNow, IsActive = true },
            new ProjectMember { ProjectId = ProjectId, UserId = ViewerId, Role = ProjectRole.Viewer, JoinedAt = DateTime.UtcNow, IsActive = true });
        db.SaveChanges();
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(userId));
        return await new AppListingService(db, authorization).ListAsync(null, default);
    }

    [Test]
    public async Task OwnerOfProjectApp_SeesItInTheirListAndCanUpdate()
    {
        var items = await ListAsync(CreatorId);
        Assert.That(items.Single().CanUpdate, Is.True);
    }

    [Test]
    public async Task ProjectDeveloper_SeesProjectAppInTheirListAndCanUpdate()
    {
        var items = await ListAsync(DeveloperId);
        Assert.That(items.Single().CanUpdate, Is.True);
    }

    [Test]
    public async Task ProjectViewer_DoesNotGetProjectAppInPersonalList()
        => Assert.That(await ListAsync(ViewerId), Is.Empty);

    [Test]
    public void RunningAnActionOnAPausedApp_MapsToConflictWithTheMessage()
    {
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new AppPausedException("The app is paused, so its Actions cannot run. Resume the app first.")
        };

        new AppActionExceptionFilter().OnException(context);

        var result = (ObjectResult)context.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(context.ExceptionHandled, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(((ProblemDetails)result.Value!).Title, Does.StartWith("The app is paused"));
        });
    }
}
