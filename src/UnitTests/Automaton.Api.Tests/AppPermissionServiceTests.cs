using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppPermissionServiceTests
{
    [Test]
    public async Task ApprovalIssuesOnlyRequestedCredentialAndRevocationInvalidatesIt()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, Guid.NewGuid(), "permission-app");
        var app = await db.WebApps.FindAsync(appId);
        app!.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract
        {
            Permissions = ["backups:read"]
        });
        await db.SaveChangesAsync();
        var service = new AppPermissionService(db, new TestProtector());

        Assert.ThrowsAsync<ArgumentException>(() => service.SetApprovalAsync(appId, "backups:write", true, default));
        var rejected = await service.SetApprovalAsync(appId, "backups:read", false, default);
        Assert.That(rejected.Single().Status, Is.EqualTo("Rejected"));
        var statuses = await service.SetApprovalAsync(appId, "backups:read", true, default);
        var token = new TestProtector().Unprotect((await db.AppPermissionApprovals.Select(row => row.ProtectedKey).SingleAsync())!);
        Assert.That(statuses.Single().Approved, Is.True);
        Assert.That((await service.ValidateCredentialAsync(token, default))?.Permission, Is.EqualTo("backups:read"));

        await service.SetApprovalAsync(appId, "backups:read", false, default);
        Assert.That(await service.ValidateCredentialAsync(token, default), Is.Null);
    }

    private sealed class TestProtector : ISecretProtector
    {
        public string Protect(string plaintext) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
        public string Unprotect(string protectedValue) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
    }
}
