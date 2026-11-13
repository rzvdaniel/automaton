using Automaton.Api.Constants;
using Automaton.Api.Controllers;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ServerConsoleProvisioningTests
{
    [Test]
    public async Task PendingPermissionsAreApprovedAndAuditedWithTheirReason()
    {
        var (service, audit, appId) = await CreateAsync(
            AppPermissionApproval.ServerRead, AppPermissionApproval.NotifyEmail, AppPermissionApproval.ServerCleanup);

        await ServerConsoleProvisioningService.ApprovePendingAsync(service, appId, default);

        var statuses = await service.GetStatusAsync(appId, default);
        Assert.That(statuses.Select(s => (s.Permission, s.Status)), Is.EquivalentTo(new[]
        {
            (AppPermissionApproval.ServerRead, AppPermissionStatuses.Approved),
            (AppPermissionApproval.NotifyEmail, AppPermissionStatuses.Approved),
            (AppPermissionApproval.ServerCleanup, AppPermissionStatuses.Approved)
        }));
        Assert.That(audit.Requests, Has.Count.EqualTo(3));
        Assert.That(audit.Requests.Select(r => r.EventType), Is.All.EqualTo(AuditEventTypes.AppPermissionApproved));
        Assert.That(audit.Requests.Select(r => r.Metadata?.ToString()), Is.All.Contain("Server Console provisioning"));
    }

    [Test]
    public async Task AnAdministratorsRejectionOrRevocationIsNotApprovedAgain()
    {
        var (service, audit, appId) = await CreateAsync(AppPermissionApproval.ServerRead, AppPermissionApproval.ServerCleanup);
        await service.SetApprovalAsync(appId, AppPermissionApproval.ServerCleanup, false, default);
        audit.Requests.Clear();

        await ServerConsoleProvisioningService.ApprovePendingAsync(service, appId, default);

        var statuses = (await service.GetStatusAsync(appId, default)).ToDictionary(s => s.Permission, s => s.Status);
        Assert.That(statuses[AppPermissionApproval.ServerRead], Is.EqualTo(AppPermissionStatuses.Approved));
        Assert.That(statuses[AppPermissionApproval.ServerCleanup], Is.EqualTo(AppPermissionStatuses.Rejected));
        Assert.That(audit.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task OnlyTheThreeServerConsolePermissionsCanBeApproved()
    {
        var (service, _, appId) = await CreateAsync(AppPermissionApproval.ServerRead, AppPermissionApproval.BackupsRead);

        await ServerConsoleProvisioningService.ApprovePendingAsync(service, appId, default);

        var statuses = (await service.GetStatusAsync(appId, default)).ToDictionary(s => s.Permission, s => s.Status);
        Assert.That(statuses[AppPermissionApproval.BackupsRead], Is.EqualTo(AppPermissionStatuses.Pending), "a permission outside the list needs a person");
        Assert.That(statuses[AppPermissionApproval.ServerRead], Is.EqualTo(AppPermissionStatuses.Approved));
    }

    [Test]
    public void TheInstallNoticeEndpointsAreForSystemAdministratorsOnly()
    {
        var authorize = typeof(ServerConsoleController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(authorize.Roles, Is.EqualTo(UserRole.SystemAdministrator));
        Assert.That(typeof(ServerConsoleController).GetMethod(nameof(ServerConsoleController.Install))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true), Is.Empty);
    }

    private static async Task<(AppPermissionService Service, RecordingAudit Audit, Guid AppId)> CreateAsync(params string[] permissions)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, Guid.NewGuid(), "server-console");
        (await db.WebApps.FindAsync(appId))!.ApplicationContractJson =
            ApplicationContractSerialization.Serialize(new ApplicationContract { Permissions = [.. permissions] });
        await db.SaveChangesAsync();
        var audit = new RecordingAudit();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        return (new AppPermissionService(db, protector, audit), audit, appId);
    }

    private sealed class RecordingAudit : IAuditEventService
    {
        public List<AuditEventWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
