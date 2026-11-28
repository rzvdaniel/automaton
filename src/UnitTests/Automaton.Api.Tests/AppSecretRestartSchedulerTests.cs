using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Automaton.Api.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

public sealed class AppSecretRestartSchedulerTests
{
    private sealed class CountingRestarter : IAppRuntimeRestarter
    {
        public int Calls;
        public Task<int> RestartRunningAsync(Guid appId, CancellationToken token, bool wait = false)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(0);
        }
    }

    [Test]
    public async Task SecretsChangedTogetherGiveOneRestart()
    {
        var restarter = new CountingRestarter();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<UserContextService>();
        services.AddScoped<IAppRuntimeRestarter>(_ => restarter);
        using var provider = services.BuildServiceProvider();
        var scheduler = new AppSecretRestartScheduler(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AppSecretRestartScheduler>.Instance);
        var app = Guid.NewGuid();

        scheduler.SecretChanged(app, Guid.NewGuid(), "A_TOKEN");
        scheduler.SecretChanged(app, Guid.NewGuid(), "B_TOKEN");
        Assert.That(restarter.Calls, Is.EqualTo(0));
        await Task.Delay(AppSecretRestartScheduler.Window + TimeSpan.FromSeconds(2));
        Assert.That(restarter.Calls, Is.EqualTo(1));

        scheduler.SecretChanged(app, Guid.NewGuid(), "A_TOKEN");
        await Task.Delay(AppSecretRestartScheduler.Window + TimeSpan.FromSeconds(2));
        Assert.That(restarter.Calls, Is.EqualTo(2));
    }
}
