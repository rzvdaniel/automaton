using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class MachineManagementRunnerIdentityTests
{
    [TestCase(RunnerPurposes.Managed, RunnerApplicationProfiles.MachineManagement, true)]
    [TestCase(RunnerPurposes.Managed, RunnerApplicationProfiles.MachineManagement, false)]
    [TestCase(RunnerPurposes.Managed, RunnerApplicationProfiles.Standard, true)]
    [TestCase(RunnerPurposes.Infrastructure, RunnerApplicationProfiles.MachineManagement, true)]
    public async Task ExecutionIdentity_RequiresEnrolledMachineManagementKey(
        string purpose, string profile, bool useEnrolledKey)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var enrolledKeyId = Guid.NewGuid();
        var runner = new Runner
        {
            Id = Guid.NewGuid(), Name = "test-runner", Purpose = purpose,
            ApplicationProfile = profile, ApiKeyId = enrolledKeyId
        };
        db.Runners.Add(runner);
        await db.SaveChangesAsync();
        var identity = new RunnerExecutionIdentityService(db, null!);
        var keyId = useEnrolledKey ? enrolledKeyId : Guid.NewGuid();

        if (purpose == RunnerPurposes.Managed && profile == RunnerApplicationProfiles.MachineManagement && useEnrolledKey)
            Assert.DoesNotThrowAsync(() => identity.EnsureAsync(keyId, runner.Id, Guid.NewGuid(), CancellationToken.None));
        else
            Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.EnsureAsync(keyId, runner.Id, Guid.NewGuid(), CancellationToken.None));
    }
}
