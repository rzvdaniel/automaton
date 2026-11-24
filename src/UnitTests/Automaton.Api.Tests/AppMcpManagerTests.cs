using Automaton.Api.Mcp;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMcpManagerTests
{
    private static AppMcpManager Create(Data.ApplicationDbContext db, Guid userId)
    {
        var users = AppAuthorizationTestData.CreateUserContextService(userId);
        return new AppMcpManager(null!, null!, null!, null!, null!, null!, null!, null!, db,
            new ResourceAuthorizationService(db, users), users, null!, null!);
    }

    [Test]
    public async Task DeployAndMissingSecrets_AnotherUsersApp_IsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        await using var db = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "owner-app");
        await db.SaveChangesAsync();
        var stranger = Create(db, Guid.NewGuid());
        var files = new Dictionary<string, string> { ["automaton.json"] = "{}" };

        var deploy = Assert.ThrowsAsync<McpUserException>(() => stranger.DeployAppAsync(appId, files, default));
        var secrets = Assert.ThrowsAsync<McpUserException>(() => stranger.MissingSecretsAsync(appId, default));

        Assert.That(deploy!.Message, Is.EqualTo(AppMcpReader.NotFound));
        Assert.That(secrets!.Message, Is.EqualTo(AppMcpReader.NotFound));
    }

    [Test]
    public void ValidateFiles_RefusesOversizeAndUnsafeAndMissingConfig()
    {
        var big = new string('x', (int)AppMcpManager.MaxDeployBytes + 1);
        Assert.That(Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["big.txt"] = big }))!.Message, Does.Contain("limit"));
        Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["../evil"] = "x" }));
        Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["/etc/passwd"] = "x" }));
        Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["app.py"] = "x" }));
        Assert.DoesNotThrow(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["src/app.py"] = "x" }));
    }
}
