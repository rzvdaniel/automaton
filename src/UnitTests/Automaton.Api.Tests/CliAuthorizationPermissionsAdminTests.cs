using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class CliAuthorizationPermissionsAdminTests
{
    private sealed class Audit : IAuditEventService
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) { Events.Add(request); return Task.CompletedTask; }
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestService(ApplicationDbContext db, Audit audit, HashSet<Guid> admins)
        : CliAuthorizationService(db, new ApiKeyService(db, NullLogger<ApiKeyService>.Instance, null!, audit), audit,
            NullLogger<CliAuthorizationService>.Instance, new EphemeralDataProtectionProvider(), null!)
    {
        public override Task<bool> IsActiveAdministratorAsync(Guid userId) => Task.FromResult(admins.Contains(userId));
    }

    private static (TestService Service, ApplicationDbContext Db, Audit Audit, HashSet<Guid> Admins) Create()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var audit = new Audit();
        var admins = new HashSet<Guid>();
        return (new TestService(db, audit, admins), db, audit, admins);
    }

    [Test]
    public async Task Administrator_ApprovesAdminRequest_GetsExpiringKey_AuditNamesApproverKeyAndMachine()
    {
        var (service, db, audit, admins) = Create();
        var admin = Guid.NewGuid();
        admins.Add(admin);
        var created = await service.CreateAsync("e2e-box", "10.0.0.9", default, ApiKeyScope.PermissionsAdmin);
        Assert.That(created.ExpiresAt - DateTime.UtcNow, Is.LessThan(TimeSpan.FromMinutes(6)));

        Assert.That(await service.MayApproveAsync(created.RequestId, created.UserCode, admin, false, default), Is.True);
        Assert.That(await service.ApproveAsync(created.RequestId, created.UserCode, admin, default), Is.True);
        var poll = await service.PollAsync(created.RequestId, created.Verifier, default);

        Assert.That(poll.Status, Is.EqualTo("authorized"));
        var key = await db.ApiKeys.SingleAsync();
        Assert.That(key.Scope, Is.EqualTo(ApiKeyScope.PermissionsAdmin));
        Assert.That((await db.ApiKeyUsers.SingleAsync()).UserId, Is.EqualTo(admin));
        Assert.That(key.ExpiresAt, Is.InRange(DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31)));
        var issued = audit.Events.Single(e => e.Outcome == "Success");
        Assert.That(issued.ActorUserId, Is.EqualTo(admin));
        Assert.That(issued.EntityId, Is.EqualTo(key.Id));
        Assert.That(System.Text.Json.JsonSerializer.Serialize(issued.Metadata), Does.Contain("e2e-box").And.Contain("\"Scope\":8"));
    }

    [Test]
    public async Task RegularUser_CannotApproveAdminRequest_NoKey_RefusalAudited()
    {
        var (service, db, audit, _) = Create();
        var user = Guid.NewGuid();
        var created = await service.CreateAsync("e2e-box", null, default, ApiKeyScope.PermissionsAdmin);

        Assert.That(await service.MayApproveAsync(created.RequestId, created.UserCode, user, false, default), Is.False);
        Assert.That((await service.PollAsync(created.RequestId, created.Verifier, default)).Status, Is.EqualTo("pending"));
        Assert.That(await db.ApiKeys.CountAsync(), Is.Zero);
        Assert.That(audit.Events.Single().Outcome, Is.EqualTo("Denied"));
    }

    [Test]
    public async Task WrongCode_IsNotTold_ItIsAnAdminRequest_AndNothingIsAudited()
    {
        var (service, _, audit, _) = Create();
        var created = await service.CreateAsync("e2e-box", null, default, ApiKeyScope.PermissionsAdmin);

        Assert.That(await service.MayApproveAsync(created.RequestId, "WRONG-CODE", Guid.NewGuid(), false, default), Is.True);
        Assert.That(audit.Events, Is.Empty);
    }

    [Test]
    public async Task AdministratorUsingAnApiKey_CannotApproveAdminRequest()
    {
        var (service, _, _, admins) = Create();
        var admin = Guid.NewGuid();
        admins.Add(admin);
        var created = await service.CreateAsync("e2e-box", null, default, ApiKeyScope.PermissionsAdmin);

        Assert.That(await service.MayApproveAsync(created.RequestId, created.UserCode, admin, true, default), Is.False);
    }

    [Test]
    public async Task ApproverLosingAdminRoleBeforePoll_GetsNoKey()
    {
        var (service, db, audit, admins) = Create();
        var admin = Guid.NewGuid();
        admins.Add(admin);
        var created = await service.CreateAsync("e2e-box", null, default, ApiKeyScope.PermissionsAdmin);
        await service.ApproveAsync(created.RequestId, created.UserCode, admin, default);
        admins.Clear();

        Assert.That((await service.PollAsync(created.RequestId, created.Verifier, default)).Status, Is.EqualTo("denied"));
        Assert.That(await db.ApiKeys.CountAsync(), Is.Zero);
        Assert.That(audit.Events.Single().Outcome, Is.EqualTo("Denied"));
    }

    [Test]
    public async Task ExpiredAdminRequest_IsRefused()
    {
        var (service, db, _, admins) = Create();
        var admin = Guid.NewGuid();
        admins.Add(admin);
        var created = await service.CreateAsync("e2e-box", null, default, ApiKeyScope.PermissionsAdmin);
        (await db.CliAuthorizationRequests.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        Assert.That(await service.ApproveAsync(created.RequestId, created.UserCode, admin, default), Is.False);
        Assert.That((await service.PollAsync(created.RequestId, created.Verifier, default)).Status, Is.EqualTo("expired"));
    }

    [Test]
    public async Task DefaultRequest_StillDeveloperKeyWithoutExpiry_AndAnyUserMayApprove()
    {
        var (service, db, _, _) = Create();
        var user = Guid.NewGuid();
        var created = await service.CreateAsync("e2e-box", null, default);

        Assert.That(await service.MayApproveAsync(created.RequestId, created.UserCode, user, false, default), Is.True);
        await service.ApproveAsync(created.RequestId, created.UserCode, user, default);
        await service.PollAsync(created.RequestId, created.Verifier, default);
        var key = await db.ApiKeys.SingleAsync();
        Assert.That(key.Scope, Is.EqualTo(ApiKeyScope.Developer));
        Assert.That(key.ExpiresAt, Is.Null);
    }

    [Test]
    public async Task DetailsNameTheScope_AndOtherScopesCannotBeRequested()
    {
        var (service, _, _, _) = Create();
        var created = await service.CreateAsync("e2e-box", null, default, ApiKeyScope.PermissionsAdmin);
        var details = await service.GetDetailsAsync(created.RequestId, created.UserCode, default);
        Assert.That(details!.Scope, Is.EqualTo("PermissionsAdmin"));
        Assert.That(details.DeviceName, Is.EqualTo("e2e-box"));

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync("x", null, default, ApiKeyScope.FullAccess));
    }

    [TestCase("FullAccess")]
    [TestCase("Runner")]
    [TestCase("99")]
    [TestCase("nonsense")]
    public async Task CreateEndpoint_RefusesOtherScopes(string scope)
    {
        var controller = new CliAuthorizationController(null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var result = await controller.CreateRequest(new CreateCliAuthorizationRequest { Scope = scope }, default);
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }
}
