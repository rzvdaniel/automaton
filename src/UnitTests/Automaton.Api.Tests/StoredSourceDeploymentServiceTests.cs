using System.IO.Compression;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Deploying an app from the source the server stores for it.</summary>
[TestFixture]
public sealed class StoredSourceDeploymentServiceTests
{
    private const string BuildableConfig =
        """{"appId":"x","application":{"build":{"image":"node:22","command":{"executable":"npm","arguments":["run","build"]}},"runtime":{"image":"node:22","command":{"executable":"node","arguments":["server.js"]}}}}""";

    [Test]
    public async Task AcceptsAnAppWithStoredSourceAsAPendingOperation()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, BuildableConfig);

        var response = await Service(fixture).AcceptAsync(
            fixture.AppId, fixture.OwnerId, fixture.OwnerId, null, false, CancellationToken.None);

        Assert.That(response.Status, Is.EqualTo(DeploymentOperationStatuses.Pending));
        var operation = await fixture.Db.DeploymentOperations.SingleAsync();
        using var archive = ZipFile.OpenRead(operation.PackagePath);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.That(names, Does.Contain("manifest.json").And.Contain("source/server.js").And.Contain("source/automaton.json"));
    }

    [Test]
    public async Task AnAppWithoutStoredSourceIsRejected()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();

        Assert.ThrowsAsync<InvalidDataException>(() => Service(fixture).AcceptAsync(
            fixture.AppId, fixture.OwnerId, fixture.OwnerId, null, false, CancellationToken.None));
        Assert.That(await fixture.Db.DeploymentOperations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task APrebuiltArtifactAppIsRejectedBecauseTheServerDoesNotStoreTheArtifact()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, """{"application":{"runtime":{"command":{"executable":"node","arguments":["server.js"]}}}}""");

        Assert.ThrowsAsync<InvalidDataException>(() => Service(fixture).AcceptAsync(
            fixture.AppId, fixture.OwnerId, fixture.OwnerId, null, false, CancellationToken.None));
    }

    [Test]
    public async Task AnInvalidStoredConfigIsRejectedWithAReason()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, "{ not json");

        Assert.ThrowsAsync<InvalidDataException>(() => Service(fixture).AcceptAsync(
            fixture.AppId, fixture.OwnerId, fixture.OwnerId, null, false, CancellationToken.None));
    }

    [Test]
    public async Task AStrangerSeesNoApp()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, BuildableConfig);

        Assert.ThrowsAsync<KeyNotFoundException>(() => Service(fixture).AcceptAsync(
            fixture.AppId, Guid.NewGuid(), fixture.OwnerId, null, false, CancellationToken.None));
        Assert.ThrowsAsync<KeyNotFoundException>(() => Service(fixture).AcceptAsync(
            Guid.NewGuid(), fixture.OwnerId, fixture.OwnerId, null, false, CancellationToken.None));
    }

    [Test]
    public async Task AViewerCanSeeButNotDeployTheApp()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        WriteSource(fixture, BuildableConfig);
        var projectId = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        fixture.Db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, fixture.OwnerId, "project"));
        fixture.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId, UserId = viewer, Role = ProjectRole.Viewer, JoinedAt = DateTime.UtcNow, IsActive = true
        });
        fixture.Db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = fixture.AppId });
        await fixture.Db.SaveChangesAsync();

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(fixture).AcceptAsync(
            fixture.AppId, viewer, fixture.OwnerId, null, false, CancellationToken.None));
    }

    private static void WriteSource(ApplicationDeploymentServiceTests.DeploymentFixture fixture, string config)
    {
        var source = Path.Combine(fixture.RootPath, "AppData", "apps", fixture.OwnerId.ToString(), fixture.AppId.ToString(), "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "automaton.json"), config);
        File.WriteAllText(Path.Combine(source, "server.js"), "console.log('hi');");
    }

    private static StoredSourceDeploymentService Service(ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        var environment = new TestWebHostEnvironment { ContentRootPath = fixture.RootPath };
        var operations = DeploymentAcceptanceServiceFactory.Create(fixture).Operations;
        var authorization = new ResourceAuthorizationService(
            fixture.Db, new UserContextService(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
        return new StoredSourceDeploymentService(
            authorization, DeploymentFixtureServiceFactory.CreateStorage(fixture.Db, environment), operations);
    }
}
