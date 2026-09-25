using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Automaton.Api.Tests;

internal static class ControllerRouteProbeHost
{
    public static Task<IHost> StartAsync(System.Reflection.Assembly controllerAssembly)
        => new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services => ConfigureServices(services, controllerAssembly));
                webBuilder.Configure(ConfigurePipeline);
            })
            .StartAsync();

    private static void ConfigureServices(IServiceCollection services, System.Reflection.Assembly controllerAssembly)
    {
        services.AddRouting();
        services.AddControllers().AddApplicationPart(controllerAssembly);
    }

    private static void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            var action = context.GetEndpoint()?
                .Metadata
                .GetMetadata<ControllerActionDescriptor>();

            if (action is not null)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync(action.ActionName);
                return;
            }

            await next();
        });
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}
