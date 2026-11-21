using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ActionWebhookServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();
    private static readonly Guid AppId = Guid.NewGuid();

    private sealed class Harness : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        public FakeExecutions Executions { get; } = new();

        public Harness()
        {
            Db.AddRange(
                new ApplicationUser { Id = OwnerId, UserName = "owner", PublicUsername = "owner" },
                new ApplicationUser { Id = OtherId, UserName = "other", PublicUsername = "other" });
            Db.SaveChanges();
            AppAuthorizationTestData.SeedApp(Db, AppId, OwnerId, "my-app");
        }

        public ActionWebhookService For(Guid userId)
            => new(Db, new ResourceAuthorizationService(Db, AppAuthorizationTestData.CreateUserContextService(userId)),
                new Resolver(), Executions, new StubAudit());

        public async Task<(ActionWebhookView Hook, string Secret)> CreateAsync()
        {
            var view = await For(OwnerId).CreateAsync(AppId, new CreateActionWebhookRequest { ActionKey = "docs-walk", Name = "Docs walk" }, OwnerId, "https://api.test", default);
            return (view, view.Secret!);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Test]
    public async Task Create_ShowsSecretOnce_AndStoresOnlyItsHash()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();

        Assert.That(hook.Url, Is.EqualTo($"https://api.test/api/hooks/{hook.Id}"));
        Assert.That(secret, Does.StartWith("whk_"));
        var stored = await h.Db.ActionWebhooks.SingleAsync();
        Assert.That(stored.SecretHash, Is.Not.EqualTo(secret).And.EqualTo(ActionWebhookService.Hash(secret)));
        var listed = await h.For(OwnerId).ListAsync(AppId, OwnerId, "https://api.test", default);
        Assert.That(listed.Single().Secret, Is.Null);
    }

    [Test]
    public async Task Call_WithSecret_StartsActionWithPostedInput_AsOwner_AndNamesTheWebhook()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();

        var result = await h.For(OwnerId).CallAsync(hook.Id, secret, """{"version":"0.2.175"}""", default);

        Assert.That(result.StatusCode, Is.EqualTo(202));
        Assert.That(h.Executions.Started, Has.Count.EqualTo(1));
        Assert.That(h.Executions.Started[0].Input, Is.EqualTo("""{"version":"0.2.175"}"""));
        Assert.That(h.Executions.Started[0].User, Is.EqualTo(OwnerId));
        Assert.That(h.Executions.Started[0].Name, Is.EqualTo("Docs walk"));
        Assert.That((await h.Db.ActionWebhookCalls.SingleAsync()).Outcome, Is.EqualTo(ActionWebhookCallOutcomes.Started));
        Assert.That((await h.Db.ActionWebhooks.SingleAsync()).LastCalledAtUtc, Is.Not.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("whk_wrong")]
    public async Task Call_WithoutTheSecret_Gets404_AndNothingRuns(string? presented)
    {
        await using var h = new Harness();
        var (hook, _) = await h.CreateAsync();

        var result = await h.For(OwnerId).CallAsync(hook.Id, presented, "{}", default);
        var unknown = await h.For(OwnerId).CallAsync(Guid.NewGuid(), "whk_x", "{}", default);

        Assert.That(result.StatusCode, Is.EqualTo(404));
        Assert.That(unknown.StatusCode, Is.EqualTo(404));
        Assert.That(result.Message, Is.EqualTo(unknown.Message));
        Assert.That(h.Executions.Started, Is.Empty);
        Assert.That(await h.Db.ActionWebhookCalls.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Call_ToDisabledWebhook_IsRefused_AndEnableRestoresIt()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();
        var service = h.For(OwnerId);
        await service.SetEnabledAsync(AppId, hook.Id, false, OwnerId, "https://api.test", default);

        Assert.That((await service.CallAsync(hook.Id, secret, "{}", default)).StatusCode, Is.EqualTo(403));
        Assert.That(h.Executions.Started, Is.Empty);

        await service.SetEnabledAsync(AppId, hook.Id, true, OwnerId, "https://api.test", default);
        Assert.That((await service.CallAsync(hook.Id, secret, "{}", default)).StatusCode, Is.EqualTo(202));
    }

    [Test]
    public async Task Rotate_MakesTheOldSecretFail_AndTheNewOneWork()
    {
        await using var h = new Harness();
        var (hook, oldSecret) = await h.CreateAsync();
        var service = h.For(OwnerId);

        var rotated = await service.RotateSecretAsync(AppId, hook.Id, OwnerId, "https://api.test", default);

        Assert.That(rotated.Secret, Is.Not.Null.And.Not.EqualTo(oldSecret));
        Assert.That((await service.CallAsync(hook.Id, oldSecret, "{}", default)).StatusCode, Is.EqualTo(404));
        Assert.That((await service.CallAsync(hook.Id, rotated.Secret, "{}", default)).StatusCode, Is.EqualTo(202));
    }

    [Test]
    public async Task Call_ToPausedApp_IsRefusedWithAClearMessage()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();
        (await h.Db.WebApps.SingleAsync()).IsPaused = true;
        await h.Db.SaveChangesAsync();

        var result = await h.For(OwnerId).CallAsync(hook.Id, secret, "{}", default);

        Assert.That(result.StatusCode, Is.EqualTo(409));
        Assert.That(result.Message, Does.Contain("paused"));
        Assert.That(h.Executions.Started, Is.Empty);
    }

    [Test]
    public async Task Call_WhileTheActionRuns_IsSkipped()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();
        h.Db.AppModuleExecutions.Add(new AppModuleExecution
        {
            Id = Guid.NewGuid(), AppId = AppId, ModuleType = AppModuleTypes.Action, ModuleKey = "docs-walk",
            TriggerType = "Manual", Status = AppModuleExecutionStatuses.Running, CreatedAtUtc = DateTime.UtcNow
        });
        await h.Db.SaveChangesAsync();

        var result = await h.For(OwnerId).CallAsync(hook.Id, secret, "{}", default);

        Assert.That(result.StatusCode, Is.EqualTo(409));
        Assert.That((await h.Db.ActionWebhookCalls.SingleAsync()).Outcome, Is.EqualTo(ActionWebhookCallOutcomes.Skipped));
        Assert.That(h.Executions.Started, Is.Empty);
    }

    [Test]
    public async Task Call_WithBadJson_OrInputThatBreaksTheSchema_Gets400()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();
        var service = h.For(OwnerId);

        Assert.That((await service.CallAsync(hook.Id, secret, "not json", default)).StatusCode, Is.EqualTo(400));
        Assert.That((await service.CallAsync(hook.Id, secret, "[1]", default)).StatusCode, Is.EqualTo(400));
        Assert.That(h.Executions.Started, Is.Empty);
    }

    [Test]
    public async Task Call_OverTheRateLimit_Gets429()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();
        for (var i = 0; i < ActionWebhookService.CallsPerMinute; i++)
            h.Db.ActionWebhookCalls.Add(new ActionWebhookCall { Id = Guid.NewGuid(), WebhookId = hook.Id, CalledAtUtc = DateTime.UtcNow, Outcome = "Skipped" });
        await h.Db.SaveChangesAsync();

        Assert.That((await h.For(OwnerId).CallAsync(hook.Id, secret, "{}", default)).StatusCode, Is.EqualTo(429));
        Assert.That(h.Executions.Started, Is.Empty);
    }

    [Test]
    public async Task Call_KeepsOnlyTheLatestCalls()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();
        var service = h.For(OwnerId);
        for (var i = 0; i < 60; i++)
            h.Db.ActionWebhookCalls.Add(new ActionWebhookCall { Id = Guid.NewGuid(), WebhookId = hook.Id, CalledAtUtc = DateTime.UtcNow.AddHours(-2).AddSeconds(i), Outcome = "Started" });
        await h.Db.SaveChangesAsync();

        await service.CallAsync(hook.Id, secret, "{}", default);

        Assert.That(await h.Db.ActionWebhookCalls.CountAsync(), Is.EqualTo(50));
    }

    [Test]
    public async Task Manage_IsForTheWebhooksOwnerOnly_AndTheAppsUsers()
    {
        await using var h = new Harness();
        var (hook, _) = await h.CreateAsync();
        var stranger = h.For(Guid.NewGuid());

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => stranger.ListAsync(AppId, Guid.NewGuid(), "https://api.test", default));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => stranger.RotateSecretAsync(AppId, hook.Id, Guid.NewGuid(), "https://api.test", default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => h.For(OwnerId).DeleteAsync(AppId, Guid.NewGuid(), OwnerId, default));
    }

    [Test]
    public async Task Delete_RemovesTheWebhookAndItsCalls_SoItsSecretStopsWorking()
    {
        await using var h = new Harness();
        var (hook, secret) = await h.CreateAsync();
        var service = h.For(OwnerId);
        await service.CallAsync(hook.Id, secret, "{}", default);

        await service.DeleteAsync(AppId, hook.Id, OwnerId, default);

        Assert.That(await h.Db.ActionWebhooks.CountAsync(), Is.Zero);
        Assert.That(await h.Db.ActionWebhookCalls.CountAsync(), Is.Zero);
        Assert.That((await service.CallAsync(hook.Id, secret, "{}", default)).StatusCode, Is.EqualTo(404));
    }

    [Test]
    public void SecretMatches_RejectsMissingHashOrSecret()
    {
        var hash = ActionWebhookService.Hash("whk_abc");
        Assert.That(ActionWebhookService.SecretMatches(hash, "whk_abc"), Is.True);
        Assert.That(ActionWebhookService.SecretMatches(hash, "whk_abd"), Is.False);
        Assert.That(ActionWebhookService.SecretMatches(null, "whk_abc"), Is.False);
        Assert.That(ActionWebhookService.SecretMatches(hash, null), Is.False);
        Assert.That(ActionWebhookService.SecretMatches(hash, ""), Is.False);
    }

    internal sealed class FakeExecutions()
        : AppActionExecutionService(null!, null!, null!, null!, null!)
    {
        public List<(string Input, Guid User, string Name)> Started { get; } = [];

        public override Task<AppModuleExecution> ExecuteWebhookAsync(
            Guid appId, string actionKey, Guid ownerUserId, JsonElement input, string webhookName, CancellationToken cancellationToken)
        {
            Started.Add((input.GetRawText(), ownerUserId, webhookName));
            return Task.FromResult(new AppModuleExecution { Id = Guid.NewGuid(), AppId = appId, ModuleKey = actionKey });
        }
    }

    private sealed class Resolver : IActionScheduleTargetResolver
    {
        public Task<AppActionManifest> ResolveAsync(Guid appId, string actionKey, Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(new AppActionManifest
            {
                Spec = new AppActionSpec { Input = new Dictionary<string, object?> { ["type"] = "object" } }
            });
    }

    private sealed class StubAudit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
