using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Automaton.Runner.Console;

internal static class RunnerHostBuilder
{
    public static IHostBuilder Create(string[] args)
        => Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.Configure<AppConfig>(context.Configuration.GetSection("AppConfig"));
                services.Configure<TunnelForwarderConfig>(context.Configuration.GetSection("TunnelForwarder"));
                RunnerBaseServicesRegistration.Add(services);
                RunnerRuntimeServicesRegistration.Add(services);
                RunnerExecutionServicesRegistration.Add(services);
            });
}
