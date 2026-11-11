using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Deleting a flow, custom step or Runner that sits in a project: only its own user is told why it is blocked.</summary>
[TestFixture]
public sealed class DeleteHintLeakTests
{
    private const string Hint = "part of one or more projects";

    private static (ApplicationDbContext Db, Guid Owner, Guid Stranger, Guid Project, Guid Resource) Seed()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var project = Guid.NewGuid();
        db.Projects.Add(AppAuthorizationTestData.CreateProject(project, owner, "project"));
        db.SaveChanges();
        return (db, owner, Guid.NewGuid(), project, Guid.NewGuid());
    }

    private static ResourceAuthorizationService Authorization(ApplicationDbContext db, Guid userId)
        => new(db, AppAuthorizationTestData.CreateUserContextService(userId));

    [Test]
    public void Runner_StrangerGetsAPlainRefusal_OwnerGetsTheExplanation()
    {
        var (db, owner, stranger, project, runner) = Seed();
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner, UserId = owner });
        db.ProjectRunners.Add(new ProjectRunner { ProjectId = project, RunnerId = runner });
        db.SaveChanges();
        RunnerDeletionPolicy Policy(Guid user) => new(db, Authorization(db, user));

        var refused = Assert.ThrowsAsync<UnauthorizedAccessException>(() => Policy(stranger).EnsureCanDeleteAsync(runner, default));
        var explained = Assert.ThrowsAsync<InvalidOperationException>(() => Policy(owner).EnsureCanDeleteAsync(runner, default));

        Assert.Multiple(() =>
        {
            Assert.That(refused!.Message, Does.Not.Contain("project"));
            Assert.That(explained!.Message, Does.Contain(Hint));
        });
    }
}
