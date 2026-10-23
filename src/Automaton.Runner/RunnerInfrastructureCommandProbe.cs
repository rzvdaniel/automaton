using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Configuration;
using System.CommandLine;
using System.CommandLine.Invocation;
using static System.Console;

namespace Automaton.Runner.Console;

internal static class RunnerInfrastructureCommandProbe
{
    public static Command Create()
    {
        var command = new Command(
            "infrastructure-command-probe",
            "Consume one local infrastructure command without acknowledging it. Development only.");
        command.SetHandler(RunAsync);
        return command;
    }

    private static async Task RunAsync(InvocationContext context)
    {
        EnsureDevelopmentEnvironment();
        var configuration = GetConfiguration();
        var consumer = await CreateConsumerAsync(configuration);
        var result = await consumer.ConsumeOneAsync(configuration, context.GetCancellationToken());
        WriteLine(System.Text.Json.JsonSerializer.Serialize(new { result.Execution, result.Result }));
    }

    private static void EnsureDevelopmentEnvironment()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            "Development",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The infrastructure command probe requires DOTNET_ENVIRONMENT=Development.");
        }
    }

    private static async Task<InfrastructureCommandConsumer> CreateConsumerAsync(IConfiguration configuration)
    {
        var storage = new FileStorageService();
        var workspace = new RunnerWorkspaceService();
        var coordinator = new AppActionExecutionCoordinator();
        var api = new InfrastructureCommandApiClient(configuration, storage);
        var executor = new TrustedInfrastructureActionExecutor(
            new AppActionDeploymentService(workspace, coordinator),
            new AppActionExecutionService(workspace, coordinator));
        var identity = await InfrastructureCommandIdentity.LoadAsync(storage);
        return new InfrastructureCommandConsumer(identity, executor, api);
    }

    private static IConfiguration GetConfiguration()
        => new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
}
