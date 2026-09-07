using System.Net;

namespace Automaton.Runner.Console.Services;

internal static class RunnerRuntimeEndpointConfiguration
{
    public const string HostAddressEnvironmentVariable = "AUTOMATON_RUNTIME_HOST_ADDRESS";

    public static string HostAddress => ReadHostAddress();

    private static string ReadHostAddress()
    {
        var value = Environment.GetEnvironmentVariable(HostAddressEnvironmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return "127.0.0.1";
        if (value.Any(char.IsWhiteSpace)
            || !IPAddress.TryParse(value, out var address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
            throw new InvalidOperationException(
                $"{HostAddressEnvironmentVariable} must be a concrete IPv4 or IPv6 host address, not '{value}'.");
        return address.ToString();
    }
}
