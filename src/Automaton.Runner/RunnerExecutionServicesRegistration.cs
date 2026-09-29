using Automaton.Core.Interfaces;
using Automaton.Core.Scripting;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console;

internal static class RunnerExecutionServicesRegistration
{
    public static void Add(IServiceCollection services)
    {
        services.AddScoped<RunnerSettingsService>();
        services.AddTransient<CustomStepExecuteService>();
        services.AddSingleton<InfrastructureCommandService>();
        services.AddSingleton<HostedRunnerDeploymentPackageDownloader>();
        services.AddSingleton<HostedRunnerDeploymentCommandProcessor>();
        services.AddSingleton<HostedRunnerInvalidCommandRecovery>();
        services.AddSingleton<HostedRunnerDeploymentCommandQueueConsumer>();
        services.AddSingleton<HostContainerExecutionService>();
        services.AddSingleton<TunnelForwarderService>();
        services.AddHostedService<InfrastructureCommandHostedService>();
        services.AddHostedService<HostedRunnerDeploymentCommandHostedService>();
        services.AddHttpClient<RunnerHostService>();
        services.AddHttpClient<RunnerSettingsService>();
        services.AddHttpClient<ScriptEngineSettingsReporter>();
        services.AddHttpClient(nameof(HostedRunnerDeploymentPackageDownloader))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient();
        services.AddHttpClient<RunnerRuntimeGatewayService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
            configuration.RegisterServicesFromAssembly(typeof(FlowExecuteService).Assembly);
        });
        services.AddScriptingForRunner();
        services.AddAutomatonCore();
        services.AddAutomatonSteps();
        services.AddTransient<FlowExecuteService>(CreateFlowExecuteService);
    }

    private static FlowExecuteService CreateFlowExecuteService(IServiceProvider provider)
    {
        var flowConverter = provider.GetRequiredService<FlowConvertService>();
        var config = provider.GetRequiredService<ConfigurationService>();
        return new FlowExecuteService(
            flowConverter,
            config.ApplicationName,
            provider.GetService<IExecutionNotificationService>(),
            provider.GetService<IWorkflowExecutionPersistenceService>());
    }
}
