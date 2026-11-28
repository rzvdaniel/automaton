using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Automaton.Api.Tests.BackendRouteConfigTestFixture;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDataGrantServiceTests
{
    private sealed class CountingRestarter : IAppRuntimeRestarter
    {
        public List<Guid> Restarted { get; } = [];
        public Task<int> RestartRunningAsync(Guid appId, CancellationToken token, bool wait = false)
        {
            Restarted.Add(appId);
            return Task.FromResult(1);
        }
    }

    private sealed record Setup(ApplicationDbContext Db, Guid SourceId, Guid GranteeId, Guid RunnerId, Guid OwnerId, Guid StrangerId)
    {
        public CountingRestarter Restarter { get; } = new();
    }

    private static string ComposeJson(params string[] volumes) => ApplicationContractSerialization.Serialize(new ApplicationContract
    {
        Compose = new ApplicationComposeSpec
        {
            PublicService = "web",
            Services = [new() { Name = "web", Image = "x/y:1", Port = 8080 }],
            Volumes = [.. volumes]
        }
    });

    private static async Task<Setup> CreateAsync()
    {
        var db = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var setup = new Setup(db, Guid.NewGuid(), Guid.NewGuid(), runnerId, Guid.NewGuid(), Guid.NewGuid());
        AddMachineRunner(db, runnerId);
        foreach (var (id, name, json) in new[] { (setup.SourceId, "Ghost", ComposeJson("content")), (setup.GranteeId, "File Browser", ComposeJson("srv")) })
        {
            var target = AppDeploymentTarget.CreateDefault(id, runnerId);
            target.GenericRuntimeDeployed = true;
            AddApp(db, id, target, json);
            db.WebApps.Local.Single(a => a.Id == id).Name = name;
            db.WebAppUsers.Add(new WebAppUser { WebAppId = id, UserId = setup.OwnerId });
        }
        await db.SaveChangesAsync();
        return setup;
    }

    private static AppDataGrantService Service(Setup setup, Guid userId)
    {
        var user = new UserContextService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
            }
        });
        return new AppDataGrantService(setup.Db, new ResourceAuthorizationService(setup.Db, user), user,
            new RecentActivityService(setup.Db, user), new AppAuthorizationNoOpAuditEventService(), setup.Restarter);
    }

    [Test]
    public async Task Grant_IsReadOnlyByDefault_AndRestartsTheReceivingApp()
    {
        var setup = await CreateAsync();
        var state = await Service(setup, setup.OwnerId).AddAsync(setup.SourceId, setup.GranteeId, "content", false, false, default);

        Assert.That(state.GrantedOut.Single().ReadWrite, Is.False);
        Assert.That(setup.Restarter.Restarted, Is.EqualTo(new[] { setup.GranteeId }));
        var mount = (await Service(setup, setup.OwnerId).ResolveMountsAsync(setup.GranteeId, setup.RunnerId, default)).Single();
        Assert.That(mount.ReadOnly, Is.True);
        Assert.That(mount.Target, Does.StartWith("/granted/ghost-").And.EndWith("/content"));
        Assert.That(mount.SourceAppId, Is.EqualTo(setup.SourceId));
    }

    [Test]
    public async Task ReadWrite_NeedsASecondConfirmation()
    {
        var setup = await CreateAsync();
        var service = Service(setup, setup.OwnerId);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(setup.SourceId, setup.GranteeId, "content", true, false, default));
        await service.AddAsync(setup.SourceId, setup.GranteeId, "content", true, true, default);
        var mount = (await service.ResolveMountsAsync(setup.GranteeId, setup.RunnerId, default)).Single();
        Assert.That(mount.ReadOnly, Is.False);
    }

    [Test]
    public async Task Grant_RejectsUnknownVolumeAndSelfGrant_AndStrangers()
    {
        var setup = await CreateAsync();
        var owner = Service(setup, setup.OwnerId);

        Assert.ThrowsAsync<InvalidOperationException>(() => owner.AddAsync(setup.SourceId, setup.GranteeId, "nope", false, false, default));
        Assert.ThrowsAsync<InvalidOperationException>(() => owner.AddAsync(setup.SourceId, setup.SourceId, "content", false, false, default));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(setup, setup.StrangerId).AddAsync(setup.SourceId, setup.GranteeId, "content", false, false, default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => owner.AddAsync(Guid.NewGuid(), setup.GranteeId, "content", false, false, default));
        Assert.That(await setup.Db.AppDataGrants.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task RemovedGrant_NoLongerMounts_AndRestartsTheReceivingApp()
    {
        var setup = await CreateAsync();
        var service = Service(setup, setup.OwnerId);
        var state = await service.AddAsync(setup.SourceId, setup.GranteeId, "content", false, false, default);

        await service.RemoveAsync(setup.GranteeId, state.GrantedOut.Single().Id, default);

        Assert.That(await service.ResolveMountsAsync(setup.GranteeId, setup.RunnerId, default), Is.Empty);
        Assert.That(setup.Restarter.Restarted, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Mounts_AreSkipped_WhenTheVolumeIsGoneOrOnAnotherRunner()
    {
        var setup = await CreateAsync();
        await Service(setup, setup.OwnerId).AddAsync(setup.SourceId, setup.GranteeId, "content", false, false, default);

        Assert.That(await Service(setup, setup.OwnerId).ResolveMountsAsync(setup.GranteeId, Guid.NewGuid(), default), Is.Empty);
        setup.Db.WebApps.Single(a => a.Id == setup.SourceId).ApplicationContractJson = ComposeJson("other");
        await setup.Db.SaveChangesAsync();
        Assert.That(await Service(setup, setup.OwnerId).ResolveMountsAsync(setup.GranteeId, setup.RunnerId, default), Is.Empty);
    }
}

[TestFixture]
public sealed class HostContainerRelayGrantedMountTests
{
    [Test]
    public void PrepareForMachine_DropsMountsTheCallerSent()
    {
        var runner = new Automaton.Api.Entities.Runner { Id = Guid.NewGuid(), Name = "m" };
        var target = AppDeploymentTarget.CreateDefault(Guid.NewGuid(), runner.Id);
        var request = new HostContainerExecutionRequest
        {
            Operation = HostContainerOperationKinds.ApplyComposeProject,
            Scope = new(Guid.Empty, Guid.Empty, target.WebAppId, target.Id),
            GrantedMounts = [new(Guid.NewGuid(), Guid.NewGuid(), "content", "/granted/x/content", false)]
        };

        var prepared = HostContainerRelayRequestPreparer.PrepareForMachine(
            request, new HostContainerRelayState(target, runner, Guid.NewGuid()), new MachineContainerResourceOptions());

        Assert.That(prepared.GrantedMounts, Is.Empty);
    }
}
