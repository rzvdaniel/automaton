using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Automaton.Api.Tests;

internal static class AppActionAccessTestHost
{
    public static Task<IHost> CreateAsync(ApplicationDbContext db, Guid userId)
        => new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services => ConfigureServices(services, db, userId));
            webBuilder.Configure(app => ConfigurePipeline(app, userId));
        }).StartAsync();

    private static void ConfigureServices(IServiceCollection services, ApplicationDbContext db, Guid userId)
    {
        services.AddSingleton(AppAuthorizationActionExecutionTestFactory.Create(db, userId));
        services.AddHttpContextAccessor();
        services.AddScoped<UserContextService>();
        services.AddAuthorization(options => options.AddPolicy("AllowApiKey", policy => policy.RequireAuthenticatedUser()));
        services.AddControllers().AddApplicationPart(typeof(AppActionsController).Assembly);
    }

    private static void ConfigurePipeline(IApplicationBuilder app, Guid userId)
    {
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
            await next();
        });
        app.UseAuthorization();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}
