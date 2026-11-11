using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console;

internal static class RunnerExecutionServicesRegistration
{
    public static void Add(IServiceCollection services)
    {
        services.AddSingleton<InfrastructureCommandService>();
        services.AddSingleton<HostContainerExecutionService>();
        services.AddSingleton<MachineStaticSitePublisher>();
        services.AddSingleton<MachineApplicationStorageCleanup>();
        services.AddSingleton<RunnerSelfUpgradeService>(provider => {
            var configuration = provider.GetRequiredService<ConfigurationService>();
            return new(() => configuration.RunnerId, Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID"));
        });
        services.AddSingleton<AppActionCommandExecutor>();
        services.AddSingleton<MachineActionDependencyPreparer>();
        services.AddSingleton<MachineActionExecutionHandler>();
        services.AddSingleton<HostContainerBuildCleanupService>();
        services.AddSingleton<HostContainerExecutionApiClient>();
        services.AddSingleton<TunnelForwarderService>();
        services.AddSingleton<IRunnerExecutionExecutor, RunnerExecutionExecutor>();
        services.AddSingleton<RunnerFileTransferExecutor>();
        services.AddSingleton(provider => new RunnerExecutionJournal(provider.GetRequiredService<FileStorageService>().GetRunnerExecutionJournalDirectory()));
        services.AddSingleton<RunnerExecutionProcessor>();
        services.AddSingleton<RunnerExecutionDeliveryHandler>();
        services.AddSingleton<RunnerExecutionQueueConsumer>();
        services.AddHostedService<RunnerExecutionHostedService>();
        services.AddHttpClient<RunnerHostService>();
        services.AddHttpClient(nameof(RunnerStorageTelemetryApiClient))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpClient();
        services.AddHttpClient<RunnerRuntimeGatewayService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
    }
}
