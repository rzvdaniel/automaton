using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class OrganizationAssignedAppAuthorizationTests
{
    [Test]
    public async Task CanExecuteAppAsync_AllowsAssignedOrganizationMemberWithoutProjectMembership()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: true);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesUnassignedOrganizationMember()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: false);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesAssignmentAfterOrganizationMembershipIsRevoked()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: true, memberIsActive: false);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesUnassignedAppUserInOrganizationProject()
    {
        await using var fixture = await AppFixture.CreateAsync(
            assignApp: false,
            projectRole: ProjectRole.AppUser);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesProjectAppUserAfterOrganizationMembershipIsRevoked()
    {
        await using var fixture = await AppFixture.CreateAsync(
            assignApp: true,
            memberIsActive: false,
            projectRole: ProjectRole.AppUser);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_PreservesPersonalProjectAppUserExecution()
    {
        await using var fixture = await AppFixture.CreateAsync(
            assignApp: false,
            projectRole: ProjectRole.AppUser,
            organizationScoped: false);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_AssignedMemberDispatchesActionButRevokedAccessDoesNot()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: true);
        var runnerId = await fixture.AssignMachineRunnerAsync();

        var gateway = new RecordingActionGateway();
        var service = AppAuthorizationActionExecutionTestFactory.Create(fixture.DbContext, fixture.UserId, gateway);
        using var input = JsonDocument.Parse("{}");

        var execution = await service.ExecuteAsync(fixture.AppId, "hello", fixture.UserId, input.RootElement, CancellationToken.None);

        Assert.That(gateway.QueuedRunnerId, Is.EqualTo(runnerId));
        Assert.That(execution.Status, Is.EqualTo(AppModuleExecutionStatuses.Dispatching));

        fixture.DbContext.OrganizationAppAssignments.Single().IsActive = false;
        await fixture.DbContext.SaveChangesAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.ExecuteAsync(
            fixture.AppId, "hello", fixture.UserId, input.RootElement, CancellationToken.None));
        Assert.That(gateway.DispatchCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_AssignmentIsDeniedAfterOrganizationMembershipRevocation()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: true, memberIsActive: false);
        var gateway = new RecordingActionGateway();
        var service = AppAuthorizationActionExecutionTestFactory.Create(fixture.DbContext, fixture.UserId, gateway);
        using var input = JsonDocument.Parse("{}");

        Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.ExecuteAsync(
            fixture.AppId, "hello", fixture.UserId, input.RootElement, CancellationToken.None));
        Assert.That(gateway.DispatchCount, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_ProjectDeveloperKeepsStandardRunnerAccessWhenAlsoAssigned()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: true, projectRole: ProjectRole.Developer);
        var runnerId = await fixture.AssignMachineRunnerAsync("Standard");
        var gateway = new RecordingActionGateway();
        var service = AppAuthorizationActionExecutionTestFactory.Create(fixture.DbContext, fixture.UserId, gateway);
        using var input = JsonDocument.Parse("{}");

        await service.ExecuteAsync(fixture.AppId, "hello", fixture.UserId, input.RootElement, CancellationToken.None);

        Assert.That(gateway.QueuedRunnerId, Is.EqualTo(runnerId));
    }

    [Test]
    public async Task ExecuteAsync_RequiresPersistedApprovalForPrivilegedHostTarget()
    {
        await using var fixture = await AppFixture.CreateAsync(
            assignApp: true, projectRole: ProjectRole.Developer);
        await fixture.AssignMachineRunnerAsync("PrivilegedHost");
        var gateway = new RecordingActionGateway();
        var service = AppAuthorizationActionExecutionTestFactory.Create(fixture.DbContext, fixture.UserId, gateway);
        using var input = JsonDocument.Parse("{}");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await service.ExecuteAsync(
            fixture.AppId, "hello", fixture.UserId, input.RootElement, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("persisted Privileged Host access approval"));
        Assert.That(gateway.DispatchCount, Is.Zero);
    }

    private sealed class AppFixture : IAsyncDisposable
    {
        private AppFixture(
            ApplicationDbContext dbContext,
            ResourceAuthorizationService authorization,
            Guid appId,
            Guid userId,
            Guid projectId)
        {
            DbContext = dbContext;
            Authorization = authorization;
            AppId = appId;
            UserId = userId;
            ProjectId = projectId;
        }

        public ApplicationDbContext DbContext { get; }
        public ResourceAuthorizationService Authorization { get; }
        public Guid AppId { get; }
        public Guid UserId { get; }
        public Guid ProjectId { get; }

        public async Task<Guid> AssignMachineRunnerAsync(string applicationProfile = RunnerApplicationProfiles.MachineManagement)
        {
            var runnerId = Guid.NewGuid();
            var target = await DbContext.AppDeploymentTargets.SingleAsync(x => x.WebAppId == AppId);
            DbContext.Runners.Add(new Runner
            {
                Id = runnerId,
                Name = "machine management",
                Purpose = RunnerPurposes.Managed,
                ApplicationProfile = applicationProfile,
                CapabilitiesJson = RunnerCapabilities.Serialize(
                    [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution], RunnerPurposes.Managed),
                LastHeartbeatAtUtc = DateTime.UtcNow
            });
            DbContext.ProjectRunners.Add(new ProjectRunner
            {
                ProjectId = ProjectId,
                RunnerId = runnerId,
                AddedAt = DateTime.UtcNow
            });
            target.ExecutionRunnerId = runnerId;
            await DbContext.SaveChangesAsync();
            return runnerId;
        }

        public static async Task<AppFixture> CreateAsync(
            bool assignApp,
            bool memberIsActive = true,
            ProjectRole? projectRole = null,
            bool organizationScoped = true)
        {
            var dbContext = AppAuthorizationTestData.CreateDbContext();
            var organizationId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            dbContext.Organizations.Add(new Organization
            {
                Id = organizationId,
                Name = "Test organization",
                OwnerId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            var project = AppAuthorizationTestData.CreateProject(projectId, Guid.NewGuid(), "project");
            project.OrganizationId = organizationScoped ? organizationId : null;
            dbContext.Projects.Add(project);
            dbContext.WebApps.Add(new WebApp
            {
                Id = appId,
                Name = "Assigned app",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                ProjectId = projectId,
                Scope = AccessScope.Project,
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            });
            dbContext.ProjectApps.Add(new ProjectApp
            {
                ProjectId = projectId,
                WebAppId = appId,
                AddedAt = DateTime.UtcNow
            });
            dbContext.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                UserId = userId,
                Role = OrganizationRole.AppUser,
                JoinedAt = DateTime.UtcNow,
                IsActive = memberIsActive
            });

            if (assignApp)
            {
                dbContext.OrganizationAppAssignments.Add(new OrganizationAppAssignment
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    AppId = appId,
                    UserId = userId,
                    AssignedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            if (projectRole.HasValue)
            {
                dbContext.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = projectId,
                    UserId = userId,
                    Role = projectRole.Value,
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            await dbContext.SaveChangesAsync();
            return new AppFixture(
                dbContext,
                new ResourceAuthorizationService(dbContext, AppAuthorizationTestData.CreateUserContextService(userId)),
                appId,
                userId,
                projectId);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class RecordingActionGateway : IAppActionExecutionGateway
    {
        public Guid QueuedRunnerId { get; private set; }
        public int DispatchCount { get; private set; }

        public Task<AppActionModulesResponse> ListModulesAsync(Guid runnerId, Guid appId, Guid targetId, string key, CancellationToken token)
            => Task.FromResult(new AppActionModulesResponse([
                new AppActionModuleInfo("hello", "Hello", null, "python", "main.py", 30, "hash", false, [], 0, null)]));

        public Task<Guid> QueueExecutionAsync(Guid runnerId, Guid requestedByUserId, Guid operationId, ExecuteAppActionModuleRequest request, CancellationToken token)
        {
            QueuedRunnerId = runnerId;
            DispatchCount++;
            return Task.FromResult(operationId);
        }

        public Task<AppActionVersionsResponse> ListVersionsAsync(Guid runnerId, Guid appId, Guid targetId, string key, CancellationToken token)
            => throw new NotSupportedException();
        public Task<AppActionVersionOperationResponse> RestoreVersionAsync(Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token)
            => throw new NotSupportedException();
        public Task<AppActionVersionOperationResponse> DeleteVersionAsync(Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token)
            => throw new NotSupportedException();
        public Task<bool> CancelExecutionAsync(Guid runnerId, Guid executionId, CancellationToken token)
            => throw new NotSupportedException();
        public Task<AppModuleExecutionArtifactResponse> ReadExecutionArtifactAsync(Guid runnerId, Guid appId, Guid targetId, string key, Guid executionId, bool result, CancellationToken token)
            => throw new NotSupportedException();
    }
}
