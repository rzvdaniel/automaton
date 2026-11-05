using Automaton.Api.Controllers;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Tests;

/// <summary>Status-code mapping of POST api/apps/{appId}/deploy.</summary>
[TestFixture]
public sealed class AppDeploymentControllerDeployTests
{
    private const string BuildableConfig =
        """{"application":{"build":{"image":"node:22","command":{"executable":"npm","arguments":["run","build"]}},"runtime":{"image":"node:22","command":{"executable":"node","arguments":["server.js"]}}}}""";

    [Test]
    public async Task AcceptsAnOwnerDeployWith202()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, BuildableConfig);

        var result = await Deploy(fixture, fixture.OwnerId, fixture.AppId);

        Assert.That(result.Result, Is.TypeOf<AcceptedAtActionResult>());
        Assert.That(((AcceptedAtActionResult)result.Result!).StatusCode, Is.EqualTo(202));
    }

    [Test]
    public async Task RefusesAnUnconfirmedEmailWith403()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, BuildableConfig);

        var result = await Deploy(fixture, fixture.OwnerId, fixture.AppId, confirmed: false);

        Assert.That(Status(result), Is.EqualTo(403));
    }

    [Test]
    public async Task RefusesAViewerWhoCanSeeTheAppWith403()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, BuildableConfig);
        var viewer = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        fixture.Db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, fixture.OwnerId, "project"));
        fixture.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId, UserId = viewer, Role = ProjectRole.Viewer, JoinedAt = DateTime.UtcNow, IsActive = true
        });
        fixture.Db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = fixture.AppId });
        await fixture.Db.SaveChangesAsync();

        Assert.That(Status(await Deploy(fixture, viewer, fixture.AppId)), Is.EqualTo(403));
    }

    [Test]
    public async Task ReturnsNotFoundForAnUnknownAppAndForAStranger()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, BuildableConfig);

        Assert.That(Status(await Deploy(fixture, fixture.OwnerId, Guid.NewGuid())), Is.EqualTo(404));
        Assert.That(Status(await Deploy(fixture, Guid.NewGuid(), fixture.AppId)), Is.EqualTo(404));
    }

    [Test]
    public async Task ReturnsBadRequestWithoutStoredSource()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();

        Assert.That(Status(await Deploy(fixture, fixture.OwnerId, fixture.AppId)), Is.EqualTo(400));
    }

    [Test]
    public async Task ReturnsBadRequestForAnInvalidStoredConfig()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, "{ not json");

        Assert.That(Status(await Deploy(fixture, fixture.OwnerId, fixture.AppId)), Is.EqualTo(400));
    }

    [Test]
    public async Task ReturnsBadRequestForAPrebuiltArtifactApp()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, """{"application":{"runtime":{"command":{"executable":"node","arguments":["server.js"]}}}}""");

        Assert.That(Status(await Deploy(fixture, fixture.OwnerId, fixture.AppId)), Is.EqualTo(400));
    }

    [Test]
    public async Task EditorDeploySourceIsStoredUnderTheProjectOwner()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        var projectId = Guid.NewGuid();
        fixture.Db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, fixture.OwnerId, "project"));
        fixture.Db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = fixture.AppId });
        var app = await fixture.Db.WebApps.FindAsync(fixture.AppId);
        app!.ProjectId = projectId;
        await fixture.Db.SaveChangesAsync();
        var finalization = new DeploymentPackageFinalizationService(fixture.Db, null!, null!, null!);

        Assert.That(await finalization.ResolveSourceOwnerIdAsync(fixture.AppId, Guid.NewGuid(), default), Is.EqualTo(fixture.OwnerId));
    }

    [Test]
    public async Task EditorDeployFinalizesManagedArtifactsForTheProjectOwner()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        var projectId = Guid.NewGuid();
        fixture.Db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, fixture.OwnerId, "project"));
        fixture.Db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = fixture.AppId });
        var app = await fixture.Db.WebApps.FindAsync(fixture.AppId);
        app!.ProjectId = projectId;
        await fixture.Db.SaveChangesAsync();
        var commands = new RecordingManagedArtifactsCommands();
        var environment = new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath() };
        var storage = DeploymentFixtureServiceFactory.CreateStorage(fixture.Db, environment);
        var finalization = new DeploymentPackageFinalizationService(
            fixture.Db, storage, new ConnectedFlowArtifactService(storage, null!, commands), null!);
        var runner = new Runner { Id = Guid.NewGuid(), Name = "runner", ApiCompatibilityVersion = "999" };

        // Later finalization steps need the full fixture; only the artifact synchronization is under test.
        try { await finalization.FinalizeAsync(fixture.AppId, Guid.NewGuid(), Guid.NewGuid(), runner, null!, ApplicationContractDefaults.Create(), default); }
        catch (NullReferenceException) { }

        Assert.That(commands.OwnerIds, Is.EqualTo(new[] { fixture.OwnerId }));
    }

    [Test]
    public async Task ManagedArtifactsAcceptTheProjectOwnerWhoIsNotLinkedToTheApp()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        var projectOwnerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        fixture.Db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, projectOwnerId, "project"));
        var app = await fixture.Db.WebApps.FindAsync(fixture.AppId);
        app!.ProjectId = projectId;
        await fixture.Db.SaveChangesAsync();
        var gateway = new ManagedApplicationArtifactGateway(fixture.Db, null!);
        var request = new SynchronizeApplicationManagedArtifactsRequest(fixture.AppId, fixture.Target.Id, []);
        var runnerId = fixture.Target.ExecutionRunnerId!.Value;

        // Authorization passes; queueing needs the Runner command pipeline, which this fixture does not have.
        Assert.ThrowsAsync<NullReferenceException>(
            () => gateway.SynchronizeAsync(runnerId, projectOwnerId, request, null, default));
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => gateway.SynchronizeAsync(runnerId, Guid.NewGuid(), request, null, default));
    }

    private sealed class RecordingManagedArtifactsCommands : IManagedApplicationArtifactCommands
    {
        public List<Guid> OwnerIds { get; } = [];

        public Task<SynchronizeApplicationManagedArtifactsResult> SynchronizeAsync(Guid runnerId, Guid ownerId,
            SynchronizeApplicationManagedArtifactsRequest request, Guid? deploymentOperationId, CancellationToken cancellationToken)
        {
            OwnerIds.Add(ownerId);
            return Task.FromResult(new SynchronizeApplicationManagedArtifactsResult(request.AppId, request.DeploymentTargetId, true));
        }
    }

    private static int? Status(ActionResult<DeploymentOperationResponse> result) =>
        (result.Result as ObjectResult)?.StatusCode ?? (result.Result as StatusCodeResult)?.StatusCode;

    private static async Task<ActionResult<DeploymentOperationResponse>> Deploy(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture, Guid userId, Guid appId, bool confirmed = true)
    {
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        var environment = new TestWebHostEnvironment { ContentRootPath = fixture.RootPath };
        var authorization = new ResourceAuthorizationService(fixture.Db, userContext);
        var stored = new StoredSourceDeploymentService(
            authorization,
            DeploymentFixtureServiceFactory.CreateStorage(fixture.Db, environment),
            DeploymentAcceptanceServiceFactory.Create(fixture).Operations);
        var controller = new AppDeploymentController(
            AppAuthorizationTestServiceFactory.CreateAppsService(fixture.Db, userId, fixture.RootPath),
            userContext, new Guard(confirmed), null!, null!, null!, stored);
        return await controller.DeployStoredSource(appId);
    }

    private static void WriteSource(ApplicationDeploymentServiceTests.DeploymentFixture fixture, string config)
    {
        var source = Path.Combine(fixture.RootPath, "AppData", "apps", fixture.OwnerId.ToString(), fixture.AppId.ToString(), "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "automaton.json"), config);
        File.WriteAllText(Path.Combine(source, "server.js"), "console.log('hi');");
    }

    private sealed class Guard(bool confirmed) : ConfirmedEmailGuardService(null!)
    {
        public override Task<bool> IsConfirmedAsync(Guid userId) => Task.FromResult(confirmed);
    }
}
