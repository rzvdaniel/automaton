using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppsControllerPrivilegedAssignmentTests
{
    [Test]
    public async Task Post_ReturnsBadRequestWhenPrivilegedRunnerIsNotApproved()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var result = await fixture.Controller.Post(new NewApp
        {
            Name = "New app",
            ExecutionRunnerId = fixture.PrivilegedRunner.Id
        });

        AssertApprovalRequired(result);
    }

    [Test]
    public async Task Put_ReturnsBadRequestWhenPrivilegedRunnerIsNotApproved()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var result = await fixture.Controller.Put(fixture.App.Id, new AppModel
        {
            Id = fixture.App.Id,
            Name = fixture.App.Name,
            Summary = fixture.App.Summary,
            Description = fixture.App.Description,
            ExecutionRunnerId = fixture.PrivilegedRunner.Id
        });

        AssertApprovalRequired(result);
    }

    [Test]
    public async Task UpdateRunner_ReturnsBadRequestWhenPrivilegedRunnerIsNotApproved()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var result = await fixture.Controller.UpdateRunner(fixture.App.Id, new UpdateRunnerRequest
        {
            ExecutionRunnerId = fixture.PrivilegedRunner.Id
        });

        AssertApprovalRequired(result);
    }

    private static void AssertApprovalRequired(IActionResult result)
    {
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(((BadRequestObjectResult)result).Value?.ToString(),
            Does.Contain("persisted Privileged Host access approval"));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }
        public AppsController Controller { get; }
        public WebApp App { get; }
        public Runner PrivilegedRunner { get; }

        private TestFixture(
            ApplicationDbContext db,
            AppsController controller,
            WebApp app,
            Runner privilegedRunner)
        {
            Db = db;
            Controller = controller;
            App = app;
            PrivilegedRunner = privilegedRunner;
        }

        public static async Task<TestFixture> CreateAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"apps-controller-{Guid.NewGuid():N}")
                .Options);
            var userId = Guid.NewGuid();
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
            };
            var accessor = new HttpContextAccessor { HttpContext = httpContext };
            var userContext = new UserContextService(accessor);
            var authorization = new ResourceAuthorizationService(db, userContext, accessor);
            var runnerAuthorization = new AppRunnerAuthorizationPolicy(db, authorization);
            var assignment = new AppRunnerAssignmentPolicy(
                db,
                new HostedRunnerAppCapacityService(db),
                runnerAuthorization,
                new AppDeploymentTargetResolver(db, authorization, runnerAuthorization));
            var appsService = new AppsService(
                db,
                userContext,
                null!,
                authorization,
                null!,
                null!,
                new NoOpAuditEventService(),
                new NoOpApplicationEventService(),
                null!,
                assignment);
            var app = new WebApp
            {
                Id = Guid.NewGuid(),
                Name = "Existing app",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            };
            var privilegedRunner = new Runner
            {
                Id = Guid.NewGuid(),
                Name = "Privileged Runner",
                Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost,
                CapabilitiesJson = RunnerCapabilities.Serialize(
                    RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed), RunnerPurposes.Managed),
                Status = RunnerStatus.Online.ToString(),
                ConnectionId = "test-connection"
            };
            db.WebApps.Add(app);
            db.WebAppUsers.Add(new WebAppUser { WebAppId = app.Id, UserId = userId });
            db.Runners.Add(privilegedRunner);
            db.RunnerUsers.Add(new RunnerUser { RunnerId = privilegedRunner.Id, UserId = userId });
            await db.SaveChangesAsync();

            var controller = new AppsController(
                appsService,
                null!,
                NullLogger<AppsController>.Instance,
                null!,
                userContext,
                null!,
                null!,
                null!,
                null!,
                new AlwaysConfirmedEmailGuardService(),
                null!,
                null!,
                Options.Create(new PublishedAppsConfig()),
                new NoOpApplicationEventService())
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
            return new TestFixture(db, controller, app, privilegedRunner);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class AlwaysConfirmedEmailGuardService : ConfirmedEmailGuardService
    {
        public AlwaysConfirmedEmailGuardService() : base(null!) { }
        public override Task<bool> IsConfirmedAsync(Guid userId) => Task.FromResult(true);
    }

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpApplicationEventService : IApplicationEventService
    {
        public Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PagedResult<ApplicationEventDto>> GetEventsAsync(
            Guid appId, ApplicationEventQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ApplicationEventDto>
            {
                Items = [], TotalCount = 0, Page = query.Page, PageSize = query.PageSize
            });
    }
}
