using System.Diagnostics;
using Automaton.Runner.Console.Connection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Tests;

internal sealed class HubRateLimitTestHost(WebApplication app, string retryAfter) : IAsyncDisposable
{
    public List<long> Attempts { get; } = [];
    public HubConnection Connection { get; private set; } = null!;

    public static async Task<HubRateLimitTestHost> CreateAsync(string retryAfter)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR();
        var application = builder.Build();
        var fixture = new HubRateLimitTestHost(application, retryAfter);
        fixture.ConfigureServer();
        await application.StartAsync();
        fixture.Connection = new HubConnectionBuilder().WithUrl(application.Urls.Single() + "/rate-limit",
            options => options.HttpMessageHandlerFactory = inner => new HubRateLimitHandler(inner)).Build();
        return fixture;
    }

    private void ConfigureServer()
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.Ordinal) == true)
            {
                Attempts.Add(Stopwatch.GetTimestamp());
                if (Attempts.Count <= 2)
                {
                    context.Response.StatusCode = 429;
                    context.Response.Headers.RetryAfter = retryAfter;
                    return;
                }
            }
            await next();
        });
        app.MapHub<RateLimitHub>("/rate-limit");
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        await app.DisposeAsync();
    }
}

public sealed class RateLimitHub : Hub;
