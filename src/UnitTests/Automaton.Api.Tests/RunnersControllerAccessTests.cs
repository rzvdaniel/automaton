using Automaton.Api.Controllers;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Someone who may not change a Runner gets a refusal, not a server error.</summary>
[TestFixture]
public sealed class RunnersControllerAccessTests
{
    [Test]
    public async Task AStrangerUpdatingSomeoneElsesRunnerIsRefused()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var stranger = Guid.NewGuid();
        var users = AppAuthorizationTestData.CreateUserContextService(stranger);
        var service = new RunnerService(db, users, new ResourceAuthorizationService(db, users),
            runnerDeletionService: null!, connectionSessionService: null!, runnerQueryService: null!,
            flowExecutionService: null!, connectionLifecycleService: null!);

        var result = await new RunnersController(service)
            .Put(Guid.NewGuid(), new Automaton.Api.Models.UpdateRunnerRequest { Name = "renamed" }, default);

        Assert.That(result, Is.TypeOf<ForbidResult>());
    }

    [TestCase("owner", true)]
    [TestCase("ProjectAdmin", false)]
    [TestCase("Developer", false)]
    [TestCase("stranger", false)]
    public async Task OnlyTheRunnersOwnerMayChangeIt(string who, bool allowed)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid runner = Guid.NewGuid(), project = Guid.NewGuid(), owner = Guid.NewGuid(), user = who == "owner" ? owner : Guid.NewGuid();
        db.RunnerUsers.Add(new Automaton.Api.Entities.RunnerUser { RunnerId = runner, UserId = owner });
        db.ProjectRunners.Add(new Automaton.Api.Entities.ProjectRunner { ProjectId = project, RunnerId = runner });
        if (Enum.TryParse<Automaton.Api.Enums.ProjectRole>(who, out var role))
            db.ProjectMembers.Add(new Automaton.Api.Entities.ProjectMember
            {
                ProjectId = project, UserId = user, Role = role, JoinedAt = DateTime.UtcNow, IsActive = true
            });
        await db.SaveChangesAsync();

        var users = AppAuthorizationTestData.CreateUserContextService(user);
        Assert.That(await new ResourceAuthorizationService(db, users).CanUpdateRunnerAsync(runner, user), Is.EqualTo(allowed));
    }

    private const string Image = "ghcr.io/rzvdaniel/automaton-runner@sha256:9c03fb8f4b85f227ed741fd7e5e51e0fddc64a9a700725900ce8ca4ffd226338";

    [TestCase("owner", true)]
    [TestCase("ProjectAdmin", false)]
    [TestCase("stranger", false)]
    public async Task TheOwnerUpgradesARunnerThatAnActiveProjectShares(string who, bool allowed)
    {
        var (db, runner, project, owner) = await SharedRunnerAsync(projectActive: true);
        var user = who == "owner" ? owner : Guid.NewGuid();
        if (who == "ProjectAdmin")
        {
            db.ProjectMembers.Add(new Automaton.Api.Entities.ProjectMember
            {
                ProjectId = project, UserId = user, Role = Automaton.Api.Enums.ProjectRole.ProjectAdmin, JoinedAt = DateTime.UtcNow, IsActive = true
            });
            await db.SaveChangesAsync();
        }
        var users = AppAuthorizationTestData.CreateUserContextService(user);
        var upgrade = new RunnerUpgradeService(db, new ResourceAuthorizationService(db, users),
            Microsoft.Extensions.Options.Options.Create(new Automaton.Api.Config.HostedRunnerOptions { DockerImage = Image }));

        if (allowed)
            Assert.That((await upgrade.PrepareAsync(runner, default)).UpgradeCommand, Does.Contain(runner.ToString("D")));
        else
            Assert.ThrowsAsync<UnauthorizedAccessException>(() => upgrade.PrepareAsync(runner, default));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task OnlyActiveProjectsKeepTheOwnerFromDeletingTheRunner(bool projectActive, bool canDelete)
    {
        var (db, runner, _, owner) = await SharedRunnerAsync(projectActive);
        var users = AppAuthorizationTestData.CreateUserContextService(owner);
        Assert.That(await new ResourceAuthorizationService(db, users).CanDeleteRunnerAsync(runner, owner), Is.EqualTo(canDelete));
    }

    [Test]
    public async Task DeletingAProjectRemovesItsRunnerLinks()
    {
        var (db, runner, project, owner) = await SharedRunnerAsync(projectActive: true);
        var users = AppAuthorizationTestData.CreateUserContextService(owner);
        var authorization = new ResourceAuthorizationService(db, users);

        Assert.That(await new ProjectService(db, users, authorization, new NoAudit()).DeleteProjectAsync(project), Is.True);

        Assert.Multiple(async () =>
        {
            Assert.That(await db.ProjectRunners.AnyAsync(link => link.RunnerId == runner), Is.False);
            Assert.That(await authorization.CanDeleteRunnerAsync(runner, owner), Is.True);
        });
    }

    private static async Task<(Automaton.Api.Data.ApplicationDbContext Db, Guid Runner, Guid Project, Guid Owner)> SharedRunnerAsync(bool projectActive)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid runner = Guid.NewGuid(), project = Guid.NewGuid(), owner = Guid.NewGuid();
        db.Runners.Add(new Automaton.Api.Entities.Runner
        {
            Id = runner, Name = "Shared", Purpose = Automaton.Core.Enums.RunnerPurposes.Managed, ConnectionId = "c", Status = "online",
            InstallationId = $"sh_{Guid.NewGuid():N}"
        });
        db.Projects.Add(new Automaton.Api.Entities.Project { Id = project, Name = "shared", DisplayName = "Shared", OwnerId = owner, IsActive = projectActive });
        db.RunnerUsers.Add(new Automaton.Api.Entities.RunnerUser { RunnerId = runner, UserId = owner });
        db.ProjectRunners.Add(new Automaton.Api.Entities.ProjectRunner { ProjectId = project, RunnerId = runner });
        await db.SaveChangesAsync();
        return (db, runner, project, owner);
    }

    private sealed class NoAudit : Automaton.Api.Services.Interfaces.IAuditEventService
    {
        public Task WriteAsync(Automaton.Api.Models.AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
