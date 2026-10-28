using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class RunnerInstallationIdentityTests
{
    private const string Installation = "sh_0123456789abcdef0123456789abcdef";

    [TestCase(null)]
    [TestCase(Installation)]
    [TestCase("sh_11111111111111111111111111111111")]
    [TestCase("")]
    public async Task UpdatesCannotReplaceEnrolledInstallationIdentity(string? requested)
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        var service = CreateService(db, owner);
        var update = new RunnerDetails { Name = "Renamed", InstallationId = requested };
        if (requested is null || requested == Installation)
            await service.Update(runner.Id, update, CancellationToken.None);
        else
            Assert.ThrowsAsync<ArgumentException>(() => service.Update(runner.Id, update, CancellationToken.None));

        var stored = await db.Runners.AsNoTracking().SingleAsync();
        Assert.That(stored.InstallationId, Is.EqualTo(Installation));
        Assert.That(stored.Name, Is.EqualTo(requested is null || requested == Installation ? "Renamed" : "Original"));
    }

    private static Runner SeedRunner(ApplicationDbContext db, Guid owner)
    {
        var runner = new Runner { Id = Guid.NewGuid(), Name = "Original", ApplicationProfile = "MachineManagement",
            InstallationId = Installation };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = owner });
        db.SaveChanges();
        return runner;
    }

    private static RunnerService CreateService(ApplicationDbContext db, Guid owner)
    {
        var user = AppAuthorizationTestData.CreateUserContextService(owner);
        return new(db, user, new ResourceAuthorizationService(db, user), null!, null!, null!, null!, null!);
    }
}
