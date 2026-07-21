using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public class SystemInfrastructureAppServiceTests
{
    [Test]
    public async Task EnsureSeeded_CreatesHiddenPlatformOwnedAppOnce()
    {
        await using var db = CreateDb();
        var service = new SystemInfrastructureAppService(db, null!, null!);

        await service.EnsureSeededAsync();
        await service.EnsureSeededAsync();

        var app = await db.WebApps.SingleAsync(item => item.Id == SystemInfrastructureAppService.AppId);
        Assert.That(app.Name, Is.EqualTo(SystemInfrastructureAppService.AppName));
        Assert.That(app.IsPublished, Is.False);
        Assert.That(await db.WebApps.CountAsync(item => item.Id == SystemInfrastructureAppService.AppId), Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteRunnerInfo_RejectsNormalRunnerBeforeDispatch()
    {
        await using var db = CreateDb();
        var runner = new Runner
        {
            Id = Guid.NewGuid(), Name = "managed", Purpose = RunnerPurposes.Managed,
            ConnectionId = "connected", Status = "Online"
        };
        db.Runners.Add(runner);
        await db.SaveChangesAsync();
        var service = new SystemInfrastructureAppService(db, null!, null!);

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteRunnerInfoAsync(runner.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("Infrastructure Runners"));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }
}
