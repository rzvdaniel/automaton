using Automaton.Api.Mcp;
using Automaton.Api.Services;
using Microsoft.Extensions.Configuration;
using Automaton.Core.Models;
using Automaton.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMcpManagerTests
{
    private static AppMcpManager Create(Data.ApplicationDbContext db, Guid userId)
    {
        var users = AppAuthorizationTestData.CreateUserContextService(userId);
        return new AppMcpManager(null!, null!, null!, null!, null!, null!, null!, null!, db,
            new ResourceAuthorizationService(db, users), users, null!, null!, null!, null!);
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

    // Synchronous on purpose: the accessor's HttpContext is async-local and would not flow out of an async helper.
    private static (AppMcpManager Manager, Guid AppId) InstalledApp(params ApplicationSecretDeclaration[] declarations)
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "catalog-app");
        db.WebApps.Single(a => a.Id == appId).ApplicationContractJson =
            ApplicationContractSerialization.Serialize(new ApplicationContract { Secrets = declarations.ToList() });
        db.SaveChanges();
        var users = AppAuthorizationTestData.CreateUserContextService(ownerId);
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var authorization = new ResourceAuthorizationService(db, users);
        var secrets = new AppSecretService(db, new SecretStore(db, protector), protector, authorization);
        return (new AppMcpManager(null!, null!, null!, null!, secrets, null!, null!, null!, db, authorization, users, null!, null!, null!,
            new ConfigurationService(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:WebsiteUrl"] = "https://studio.example.com" }).Build())), appId);
    }

    [Test]
    public async Task AfterInstall_RequiredSecretMissing_IsNotDeployedAndNamesIt()
    {
        var (manager, appId) = InstalledApp(
            new ApplicationSecretDeclaration { Name = "NEEDED_TOKEN" }, new ApplicationSecretDeclaration { Name = "EXTRA", Optional = true });

        var result = await manager.AfterInstallAsync(appId, "x", default);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.That(json, Does.Contain("\"deployed\":false").And.Contain("NEEDED_TOKEN").And.Not.Contain("EXTRA"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AfterInstall_NoRequiredSecretMissing_TriesToDeploy(bool optionalOnly)
    {
        var (manager, appId) = InstalledApp(optionalOnly ? [new ApplicationSecretDeclaration { Name = "EXTRA", Optional = true }] : []);

        // The deploy service is not wired in this fixture, so reaching it is what shows the app was not held back.
        var ex = Assert.ThrowsAsync<McpUserException>(() => manager.AfterInstallAsync(appId, "x", default));
        Assert.That(ex!.Message, Does.Contain("Deploying stored source"));
    }

    [Test]
    public void ValidateFiles_RefusesOversizeAndUnsafeAndMissingConfig()
    {
        var big = new string('x', 1025);
        Assert.That(Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["big.txt"] = big }, maxBytes: 1024))!.Message, Does.Contain("limit"));
        Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["../evil"] = "x" }));
        Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["/etc/passwd"] = "x" }));
        Assert.Throws<McpUserException>(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["app.py"] = "x" }));
        Assert.DoesNotThrow(() => AppMcpManager.ValidateFiles(new Dictionary<string, string> { ["automaton.json"] = "{}", ["src/app.py"] = "x" }));
    }

    private static Models.RunnerDetails R(string name, bool online = true) => new()
    {
        Id = Guid.NewGuid(), Name = name, Status = online ? Core.Enums.RunnerStatus.Online : Core.Enums.RunnerStatus.Offline,
        Purpose = Core.Enums.RunnerPurposes.Managed, ApplicationProfile = "MachineManagement", Capabilities = [Core.Enums.RunnerCapabilities.ApplicationHosting],
    };

    [Test]
    public void SelectOnlyAvailableRunner_OneOnline_IsChosen()
    {
        var one = R("a");
        Assert.That(AppMcpManager.SelectOnlyAvailableRunner([one, R("off", online: false)], null), Is.EqualTo(one.Id));
    }

    [Test]
    public void SelectOnlyAvailableRunner_NoneOrSeveral_AsksTheCaller()
    {
        Assert.That(Assert.Throws<McpUserException>(() => AppMcpManager.SelectOnlyAvailableRunner([R("off", false)], "https://s/apps/1"))!.Message, Does.Contain("no online"));
        var several = Assert.Throws<McpUserException>(() => AppMcpManager.SelectOnlyAvailableRunner([R("one"), R("two")], null))!.Message;
        Assert.That(several, Does.Contain("one").And.Contain("two"));
    }

    [Test]
    public void StudioLink_UsesTheConfiguredStudioHost()
    {
        var id = Guid.NewGuid();
        Assert.That(AppMcpManager.StudioLink("https://studio.example.test/", id), Is.EqualTo($"https://studio.example.test/apps/{id}"));
        Assert.That(AppMcpManager.StudioLink("", id), Is.Null);
    }
}
