namespace Automaton.Runner.Console.Services;

internal static class RunnerRuntimeEndpointConfiguration
{
    public const string HostAddressEnvironmentVariable = "AUTOMATON_RUNTIME_HOST_ADDRESS";
    public const string PublishAddressEnvironmentVariable = "AUTOMATON_RUNTIME_PUBLISH_ADDRESS";

    public static string HostAddress => Read(HostAddressEnvironmentVariable);
    public static string PublishAddress => Read(PublishAddressEnvironmentVariable);

    private static string Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
            return "127.0.0.1";
        return value;
    }
}
