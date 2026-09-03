namespace Automaton.Runner.Console.Services;

internal static class RunnerPlatformPorts
{
    public const int DefaultRuntimeGatewayPort = 8080;

    public static int GetRuntimeGatewayPort()
    {
        var configuredPort = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT");
        return int.TryParse(configuredPort, out var port) && port is > 0 and <= 65_535
            ? port
            : DefaultRuntimeGatewayPort;
    }
}
