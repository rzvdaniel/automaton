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

/// <summary>Hosts the flow and custom-step controllers in a real pipeline, so exception filters run.</summary>
internal static class FlowStepHttpTestHost
{
    public static Task<IHost> CreateAsync(ApplicationDbContext db, Guid userId)
        => new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddHttpContextAccessor();
                services.AddScoped<UserContextService>();
                services.AddScoped(sp => new ResourceAuthorizationService(db, sp.GetRequiredService<UserContextService>()));
                services.AddScoped(sp => new FlowsService(db, null!, null!, sp.GetRequiredService<UserContextService>(),
                    sp.GetRequiredService<ResourceAuthorizationService>(), null!));
                services.AddScoped(sp => new CustomStepsService(db, sp.GetRequiredService<UserContextService>(), null!, null!,
                    sp.GetRequiredService<ResourceAuthorizationService>(), null!));
                services.AddControllers().AddApplicationPart(typeof(FlowsController).Assembly);
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.Use(async (context, next) =>
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
                    await next();
                });
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        }).StartAsync();
}
