using System.Net;
using Microsoft.AspNetCore.TestHost;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppActionAccessHttpTests
{
    [TestCase("", true, HttpStatusCode.OK)]
    [TestCase("/logs", true, HttpStatusCode.OK)]
    [TestCase("/result", true, HttpStatusCode.OK)]
    [TestCase("", false, HttpStatusCode.NotFound)]
    [TestCase("/logs", false, HttpStatusCode.NotFound)]
    [TestCase("/result", false, HttpStatusCode.NotFound)]
    [TestCase("/cancel", false, HttpStatusCode.Forbidden)]
    public async Task ExecutionAccess_UsesResourceAuthorizationAndExpectedHttpStatus(
        string suffix, bool isOwner, HttpStatusCode expectedStatus)
    {
        var ownerId = Guid.NewGuid();
        var userId = isOwner ? ownerId : Guid.NewGuid();
        var appId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        await using var db = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "owner-app");
        db.AppModuleExecutions.Add(new()
        {
            Id = executionId, AppId = appId, RequestedByUserId = ownerId,
            ModuleKey = "owner-private-action", Status = "Succeeded"
        });
        await db.SaveChangesAsync();
        using var host = await AppActionAccessTestHost.CreateAsync(db, userId);
        var path = $"/api/apps/{appId}/action-executions/{executionId}{suffix}";
        using var response = suffix == "/cancel"
            ? await host.GetTestClient().PostAsync(path, null)
            : await host.GetTestClient().GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        if (!isOwner)
        {
            Assert.That(body, Does.Not.Contain("owner-private-action"));
            Assert.That(body, Does.Not.Contain(ownerId.ToString()));
        }
    }

}
