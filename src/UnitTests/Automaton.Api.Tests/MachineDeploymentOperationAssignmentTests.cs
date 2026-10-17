using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class MachineDeploymentOperationAssignmentTests
{
    [Test]
    public async Task ProjectDeveloperCanDispatchDeploymentWithoutPersonalRunnerRecord()
    {
        await using var fixture = await Fixture.CreateAsync();

        var assignment = await fixture.Service.AssignmentAsync(
            fixture.Runner.ApiKeyId!.Value, fixture.TransferOperationId, CancellationToken.None,
            authenticatedUserId: fixture.ActorId, runnerId: fixture.Runner.Id);

        Assert.That(assignment.Disposition, Is.EqualTo("execute"));
        Assert.That(assignment.Request?.FileTransfer?.DeploymentTargetId, Is.EqualTo(fixture.Target.Id));
    }

    [Test]
    public async Task TwoProjectDevelopersCanDeployThroughTheSameMachineRunner()
    {
        await using var fixture = await Fixture.CreateAsync();
        var secondOperationId = await fixture.QueueSecondaryDeveloperDeploymentAsync();

        var assignment = await fixture.Service.AssignmentAsync(
            fixture.Runner.ApiKeyId!.Value, secondOperationId, CancellationToken.None,
            authenticatedUserId: fixture.SecondActorId, runnerId: fixture.Runner.Id);

        Assert.That(assignment.Disposition, Is.EqualTo("execute"));
        Assert.That(assignment.Request?.FileTransfer?.AppId, Is.EqualTo(fixture.App.Id));
        Assert.That(fixture.Target.ExecutionRunnerId, Is.EqualTo(fixture.Runner.Id));
    }

    [TestCase("workspace")]
    [TestCase("target")]
    [TestCase("attempt")]
    public async Task QueuedDeploymentIsTerminalizedWhenItsBindingChanges(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.RevokeOrReplace(change);

        var assignment = await fixture.Service.AssignmentAsync(
            fixture.Runner.ApiKeyId!.Value, fixture.TransferOperationId, CancellationToken.None,
            authenticatedUserId: fixture.ActorId, runnerId: fixture.Runner.Id);
        var result = await fixture.Service.WaitAsync(fixture.TransferOperationId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Disposition, Is.EqualTo("already-applied"));
            Assert.That(assignment.Request, Is.Null);
            Assert.That(result.Outcome, Is.EqualTo("Failed"));
            Assert.That(result.FileTransferResult!.Success, Is.False);
            Assert.That(result.Error, Does.Contain(change == "attempt" ? "attempt" : change == "target" ? "assigned" : "permission"));
        });
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ApplicationDbContext db;
        private readonly Guid projectId = Guid.NewGuid();
        private readonly Guid deploymentOperationId = Guid.NewGuid();

        public Runner Runner { get; } = new()
        {
            Id = Guid.NewGuid(), ApiKeyId = Guid.NewGuid(), Name = "machine",
            Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            CapabilitiesJson = "[\"ApplicationHosting\",\"ManagedContainerExecution\"]"
        };
        public WebApp App { get; } = new() { Id = Guid.NewGuid(), Name = "project app", Summary = "", Description = "", Definition = "" };
        public AppDeploymentTarget Target { get; private set; } = null!;
        public Guid ActorId { get; } = Guid.NewGuid();
        public Guid SecondActorId { get; } = Guid.NewGuid();
        public Guid AttemptId { get; } = Guid.NewGuid();
        public Guid TransferOperationId { get; } = Guid.NewGuid();
        public RunnerExecutionOperationService Service { get; }

        private Fixture(ApplicationDbContext context)
        {
            db = context;
            App.ProjectId = projectId;
            Target = AppDeploymentTarget.CreateLive(App.Id, Runner.Id);
            Target.Status = DeploymentTargetStatus.Deploying;
            Target.ActiveDeploymentOperationId = deploymentOperationId;
            Target.ActiveDeploymentExecutionAttemptId = AttemptId;
            db.Runners.Add(Runner);
            db.WebApps.Add(App);
            db.Projects.Add(new Project { Id = projectId, OwnerId = Guid.NewGuid(), Name = "workspace", DisplayName = "Workspace" });
            db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = App.Id });
            db.ProjectMembers.AddRange(
                new ProjectMember { ProjectId = projectId, UserId = ActorId, Role = ProjectRole.Developer, IsActive = true },
                new ProjectMember { ProjectId = projectId, UserId = SecondActorId, Role = ProjectRole.Developer, IsActive = true });
            db.ProjectRunners.Add(new ProjectRunner { ProjectId = projectId, RunnerId = Runner.Id });
            db.AppDeploymentTargets.Add(Target);
            db.DeploymentOperations.Add(new DeploymentOperation
            {
                Id = deploymentOperationId, WebAppId = App.Id, OwnerUserId = ActorId,
                Status = DeploymentOperationStatuses.Running, TargetSlug = null,
                ExecutionAttemptId = AttemptId, PackagePath = "fixture.zip"
            });
            db.SaveChanges();
            Service = RunnerExecutionTestServices.Operations(db, protection: new EphemeralDataProtectionProvider());
        }

        public static Task<Fixture> CreateAsync()
        {
            var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var fixture = new Fixture(context);
            return fixture.QueueAsync();
        }

        private async Task<Fixture> QueueAsync()
        {
            var request = new RunnerFileTransferCommand(
                RunnerFileTransferKinds.DeployPackage, App.Id, "package",
                DeploymentTargetId: Target.Id, DeploymentOperationId: deploymentOperationId, ExecutionAttemptId: AttemptId);
            await Service.QueueAsync(Runner.Id, ActorId, new(null, null, FileTransfer: request), CancellationToken.None, TransferOperationId);
            return this;
        }

        public async Task<Guid> QueueSecondaryDeveloperDeploymentAsync()
        {
            var operationId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            Target.ActiveDeploymentOperationId = operationId;
            Target.ActiveDeploymentExecutionAttemptId = attemptId;
            db.DeploymentOperations.Add(new DeploymentOperation
            {
                Id = operationId, WebAppId = App.Id, OwnerUserId = SecondActorId,
                Status = DeploymentOperationStatuses.Running, PackagePath = "second-fixture.zip",
                ExecutionAttemptId = attemptId
            });
            await db.SaveChangesAsync();
            var transferId = Guid.NewGuid();
            await Service.QueueAsync(Runner.Id, SecondActorId,
                new(null, null, FileTransfer: new RunnerFileTransferCommand(
                    RunnerFileTransferKinds.DeployPackage, App.Id, "second-package",
                    DeploymentTargetId: Target.Id, DeploymentOperationId: operationId, ExecutionAttemptId: attemptId)),
                CancellationToken.None, transferId);
            return transferId;
        }

        public void RevokeOrReplace(string change)
        {
            switch (change)
            {
                case "workspace":
                    db.ProjectMembers.RemoveRange(db.ProjectMembers.Where(member => member.ProjectId == projectId && member.UserId == ActorId));
                    break;
                case "target":
                    Target.ExecutionRunnerId = Guid.NewGuid();
                    break;
                case "attempt":
                    Target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            db.SaveChanges();
        }

        public async ValueTask DisposeAsync() => await db.DisposeAsync();
    }
}
