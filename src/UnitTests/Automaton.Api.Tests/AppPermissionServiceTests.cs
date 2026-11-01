using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppPermissionServiceTests
{
    [Test]
    public async Task ApprovalIssuesOnlyRequestedCredentialAndRevocationInvalidatesIt()
    {
        var (db, appId, service, protector) = await CreateAsync();

        Assert.ThrowsAsync<ArgumentException>(() => service.SetApprovalAsync(appId, "backups:write", true, default));
        var rejected = await service.SetApprovalAsync(appId, AppPermissionApproval.BackupsRead, false, default);
        Assert.That(rejected.Single().Status, Is.EqualTo(AppPermissionStatuses.Rejected));
        var statuses = await service.SetApprovalAsync(appId, AppPermissionApproval.BackupsRead, true, default);
        var token = await IssuedTokenAsync(db, protector);
        Assert.That(statuses.Single().Approved, Is.True);
        Assert.That((await service.ValidateCredentialAsync(token, default))?.Permission, Is.EqualTo(AppPermissionApproval.BackupsRead));

        await service.SetApprovalAsync(appId, AppPermissionApproval.BackupsRead, false, default);
        Assert.That(await service.ValidateCredentialAsync(token, default), Is.Null);
    }

    [Test]
    public async Task ApprovedCredentialIsDeliveredUntilTheDeploymentWithdrawsTheRequest()
    {
        var (db, appId, service, protector) = await CreateAsync();
        var secrets = new AppSecretService(db, new SecretStore(db, protector), protector, null!);
        Assert.That(await secrets.ResolveForExecutionAsync(appId, default), Does.Not.ContainKey(AppPermissionApproval.CredentialVariable));

        await service.SetApprovalAsync(appId, AppPermissionApproval.BackupsRead, true, default);
        var token = await IssuedTokenAsync(db, protector);
        Assert.That((await secrets.ResolveForExecutionAsync(appId, default))[AppPermissionApproval.CredentialVariable], Is.EqualTo(token));

        await new DeploymentPackageFinalizationService(db, null!, null!, null!)
            .PersistSecretDeclarationsAsync(appId, new ApplicationContract(), default);

        Assert.Multiple(async () =>
        {
            Assert.That(await service.ValidateCredentialAsync(token, default), Is.Null);
            Assert.That(await secrets.ResolveForExecutionAsync(appId, default), Does.Not.ContainKey(AppPermissionApproval.CredentialVariable));
            Assert.That((await db.AppPermissionApprovals.SingleAsync()).Status, Is.EqualTo(AppPermissionStatuses.Revoked));
        });
    }

    [Test]
    public async Task SuspendedAppOrUnreadableContractGetsNoAccessInsteadOfAnError()
    {
        var (db, appId, service, protector) = await CreateAsync();
        await service.SetApprovalAsync(appId, AppPermissionApproval.BackupsRead, true, default);
        var token = await IssuedTokenAsync(db, protector);
        var app = await db.WebApps.SingleAsync(item => item.Id == appId);

        app.AccessStatus = AppAccessStatuses.Disabled;
        await db.SaveChangesAsync();
        Assert.That(await service.ValidateCredentialAsync(token, default), Is.Null);

        app.AccessStatus = AppAccessStatuses.Active;
        app.ApplicationContractJson = "{\"frontend\":{}}";
        await db.SaveChangesAsync();
        Assert.That(await service.ValidateCredentialAsync(token, default), Is.Null);
        Assert.That(await service.GetStatusAsync(appId, default), Is.Empty);
    }

    private static async Task<(Automaton.Api.Data.ApplicationDbContext Db, Guid AppId, AppPermissionService Service, DataProtectionSecretProtector Protector)> CreateAsync()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, Guid.NewGuid(), "permission-app");
        var app = await db.WebApps.FindAsync(appId);
        app!.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract
        {
            Permissions = [AppPermissionApproval.BackupsRead]
        });
        await db.SaveChangesAsync();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        return (db, appId, new AppPermissionService(db, protector), protector);
    }

    private static async Task<string> IssuedTokenAsync(Automaton.Api.Data.ApplicationDbContext db, DataProtectionSecretProtector protector)
        => protector.Unprotect((await db.AppPermissionApprovals.Select(row => row.ProtectedKey).SingleAsync())!);
}
