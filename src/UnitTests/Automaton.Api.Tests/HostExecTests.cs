using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostExecTests
{
    private sealed class Audit : IAuditEventService
    {
        public List<(string Action, string Message, string Metadata)> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            Events.Add((request.Action, request.Message, System.Text.Json.JsonSerializer.Serialize(request.Metadata)));
            return Task.CompletedTask;
        }
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
        {
            Events.Add((action, message, System.Text.Json.JsonSerializer.Serialize(metadata)));
            return Task.CompletedTask;
        }
    }

    private sealed class Commands : IHostExecCommands
    {
        public List<HostExecRequest> Sent { get; } = [];
        public Task<HostExecResult> SendAsync(Guid runnerId, Guid actor, HostExecRequest request, TimeSpan timeout, CancellationToken token)
        {
            Sent.Add(request);
            return Task.FromResult(new HostExecResult(0, false, "0\n", "", DateTime.UtcNow, DateTime.UtcNow));
        }
    }

    private sealed class Setup
    {
        public required ApplicationDbContext Db { get; init; }
        public required HostExecService Service { get; init; }
        public required Commands Commands { get; init; }
        public required Audit Audit { get; init; }
        public Guid AppId { get; } = Guid.NewGuid();
        public Guid Admin { get; } = Guid.NewGuid();
        public Guid Runner { get; } = Guid.NewGuid();
        public Task<HostExecResult> Run(string script = "id -u", string? stdin = null, int timeout = 60) =>
            Service.RunAsync(AppId, Admin, Runner, new HostExecRequest(script, stdin, timeout), default);
    }

    private static async Task<Setup> SetUp(bool ownerIsAdmin = true, bool enabled = true)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var audit = new Audit();
        var commands = new Commands();
        var authorization = new ResourceAuthorizationService(db, new UserContextService(new HttpContextAccessor()));
        var permissions = new AppPermissionService(db, new Protector(), audit);
        var setup = new Setup { Db = db, Audit = audit, Commands = commands, Service = new HostExecService(db, permissions, authorization, audit, commands) };
        var owner = ownerIsAdmin ? setup.Admin : Guid.NewGuid();
        await InternalRuntimeControllerTests.SeedRestrictedAppAsync(db, setup.AppId, owner);
        db.WebApps.Single(app => app.Id == setup.AppId).ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract { Permissions = [AppPermissionApproval.RunnerHostExec] });
        var role = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = role, Name = UserRole.SystemAdministrator, NormalizedName = UserRole.SystemAdministrator.ToUpperInvariant() });
        db.UserRoles.Add(new ApplicationUserRole { UserId = setup.Admin, RoleId = role });
        db.Runners.Add(new Runner
        {
            Id = setup.Runner, Name = "runner-01", Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            InstallationId = "sh_x", ConnectionId = "c", LastHeartbeatAtUtc = DateTime.UtcNow, ApiCompatibilityVersion = "7"
        });
        await db.SaveChangesAsync();
        if (enabled) await setup.Service.SetEnabledAsync(true, setup.Admin, default);
        return setup;
    }

    private sealed class Protector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string protectedValue) => protectedValue["enc:".Length..];
    }

    [Test]
    public async Task AnApprovedAdminAppRunsTheScriptAndTheRunIsAuditLoggedWithoutTheScript()
    {
        var s = await SetUp();
        await s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default);
        var result = await s.Run("echo SECRET-SCRIPT-TEXT", stdin: "tok-123");
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(s.Commands.Sent.Single().Stdin, Is.EqualTo("tok-123"));
        var logged = string.Join("\n", s.Audit.Events.Select(e => e.Message + e.Metadata));
        Assert.That(logged, Does.Contain("runner-01").And.Contain("scriptSha256").And.Contain("exitCode"));
        Assert.That(logged, Does.Not.Contain("SECRET-SCRIPT-TEXT").And.Not.Contain("tok-123"));
        Assert.That(s.Audit.Events.Select(e => e.Action), Does.Contain("Started").And.Contain("Succeeded"));
    }

    [Test]
    public async Task WithTheSwitchOffARunIsRefusedAndApprovalIsNotPossible()
    {
        var s = await SetUp(enabled: false);
        Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default));
        Assert.ThrowsAsync<HostExecRefusedException>(() => s.Run());
        Assert.That(s.Commands.Sent, Is.Empty);
    }

    [Test]
    public async Task TurningTheSwitchOffRevokesEveryApproval()
    {
        var s = await SetUp();
        await s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default);
        await s.Service.SetEnabledAsync(false, s.Admin, default);
        Assert.That(s.Db.AppRunnerPermissions.Any(), Is.False);
        await s.Service.SetEnabledAsync(true, s.Admin, default);
        Assert.ThrowsAsync<HostExecRefusedException>(() => s.Run());
    }

    [Test]
    public async Task AnAppOwnedByAnOrdinaryUserCannotBeApprovedOrRun()
    {
        var s = await SetUp(ownerIsAdmin: false);
        Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default));
        Assert.ThrowsAsync<HostExecRefusedException>(() => s.Run());
        Assert.That(s.Commands.Sent, Is.Empty);
    }

    [Test]
    public async Task AMachineWithoutApprovalIsRefused()
    {
        var s = await SetUp();
        Assert.ThrowsAsync<HostExecRefusedException>(() => s.Run());
        Assert.That(s.Commands.Sent, Is.Empty);
    }

    [Test]
    public async Task ANewDeploymentSuspendsTheApprovalUntilItIsApprovedAgain()
    {
        var s = await SetUp();
        await s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default);
        await s.Run();
        var app = s.Db.WebApps.Single(item => item.Id == s.AppId);
        app.Version++;
        await s.Db.SaveChangesAsync();
        var refused = Assert.ThrowsAsync<HostExecRefusedException>(() => s.Run());
        Assert.That(refused!.Message, Does.Contain("approve it again"));
        Assert.That((await s.Service.ListAsync(s.AppId, default)).Single().Suspended, Is.True);
        await s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default);
        Assert.That((await s.Run()).ExitCode, Is.EqualTo(0));
    }

    [Test]
    public async Task ChangingTheOwnerRevokesTheApproval()
    {
        var s = await SetUp();
        await s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default);
        var link = s.Db.WebAppUsers.Single(item => item.WebAppId == s.AppId);
        s.Db.WebAppUsers.Remove(link);
        s.Db.WebAppUsers.Add(new WebAppUser { WebAppId = s.AppId, UserId = Guid.NewGuid() });
        await s.Db.SaveChangesAsync();
        Assert.ThrowsAsync<HostExecRefusedException>(() => s.Run());
        Assert.That(s.Db.AppRunnerPermissions.Any(), Is.False);
    }

    [TestCase("", 60)]
    [TestCase("id", 0)]
    [TestCase("id", 7201)]
    public async Task ABadScriptOrTimeoutIsRejectedBeforeAnythingRuns(string script, int timeout)
    {
        var s = await SetUp();
        await s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default);
        Assert.ThrowsAsync<ArgumentException>(() => s.Run(script, timeout: timeout < 1 ? -1 : timeout));
        Assert.That(s.Commands.Sent, Is.Empty);
    }

    [Test]
    public async Task ARunnerThatIsTooOldIsRefused()
    {
        var s = await SetUp();
        await s.Service.SetAsync(s.AppId, s.Runner, true, s.Admin, default);
        s.Db.Runners.Single().ApiCompatibilityVersion = "6";
        await s.Db.SaveChangesAsync();
        Assert.ThrowsAsync<InvalidOperationException>(() => s.Run());
    }
}
