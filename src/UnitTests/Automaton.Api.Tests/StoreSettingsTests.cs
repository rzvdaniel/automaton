using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Middleware;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class StoreSettingsTests
{
    [Test]
    public async Task Store_IsPublicByDefault()
    {
        await using var db = CreateDbContext();

        var settings = await CreateService(db).GetStoreSettingsAsync();

        Assert.That(settings.Mode, Is.EqualTo(StoreSettings.Public));
    }

    [Test]
    public async Task SaveStoreSettings_NormalizesAndPersistsDisabled()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);

        await service.SaveStoreSettingsAsync(new StoreSettings { Mode = " Disabled " });

        Assert.That((await service.GetStoreSettingsAsync()).IsEnabled, Is.False);
    }

    [Test]
    public async Task SaveStoreSettings_RejectsUnknownModes()
    {
        await using var db = CreateDbContext();

        Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).SaveStoreSettingsAsync(new StoreSettings { Mode = "members-only" }));
        Assert.That(await db.Settings.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task PublicStatus_ReportsDisabledStore()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        await service.SaveStoreSettingsAsync(new StoreSettings { Mode = StoreSettings.Disabled });

        var result = await new PublicStoreController(service).GetStatus();

        var status = (result.Result as OkObjectResult)?.Value as PublicStoreStatusDto;
        Assert.That(status?.Enabled, Is.False);
    }

    [TestCase(StoreSettings.Public, false)]
    [TestCase(StoreSettings.Disabled, true)]
    public async Task RequireStoreEnabled_BlocksCatalogOnlyWhenDisabled(string mode, bool blocked)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        await service.SaveStoreSettingsAsync(new StoreSettings { Mode = mode });
        var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var filter = (IAsyncActionFilter)new RequireStoreEnabledAttribute().CreateInstance(provider);
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: null!);
        var executed = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: null!));
        });

        Assert.Multiple(() =>
        {
            Assert.That(executed, Is.EqualTo(!blocked));
            Assert.That(context.Result is NotFoundObjectResult, Is.EqualTo(blocked));
        });
    }

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SettingsService CreateService(ApplicationDbContext db)
        => new(db, Options.Create(new AppFileRuleBaselineOptions()));
}
