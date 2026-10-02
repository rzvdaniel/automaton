using System.Globalization;
using System.Net;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class HostContainerRuntimeEndpoint
{
    public static HostContainerExecutionResponse Read(
        JsonElement root,
        string id,
        bool running,
        Dictionary<string, string> labels)
    {
        var address = GetPrivateRuntimeHostAddress();
        var portBinding = ReadRequestedPortBinding(root, address);
        var hostPort = ReadEffectiveHostPort(root, portBinding.BindingName, address);
        if (running && !hostPort.HasValue) throw new JsonException("Running container has no effective publication.");
        return new HostContainerExecutionResponse
        {
            Success = true, Exists = true, Running = running, ContainerId = id, Labels = labels,
            ContainerPort = portBinding.ContainerPort, HostPort = hostPort, HostAddress = hostPort.HasValue ? address : null
        };
    }

    private static (string BindingName, int ContainerPort) ReadRequestedPortBinding(JsonElement root, string address)
    {
        var hostConfig = root.GetProperty("HostConfig");
        if (hostConfig.GetProperty("NetworkMode").GetString() != "bridge") throw new JsonException("Unexpected runtime network.");
        var configured = hostConfig.GetProperty("PortBindings").EnumerateObject().ToArray();
        if (configured.Length != 1 || !TryParseTcpPort(configured[0].Name, out var containerPort))
            throw new JsonException("Expected one TCP publication.");
        var binding = SingleBinding(configured[0].Value);
        RequireAddress(binding, address);
        ParseHostPort(binding);
        return (configured[0].Name, containerPort);
    }

    private static int? ReadEffectiveHostPort(JsonElement root, string bindingName, string address)
    {
        var ports = root.GetProperty("NetworkSettings").GetProperty("Ports");
        if (ports.ValueKind == JsonValueKind.Null) return null;
        int? hostPort = null;
        foreach (var port in ports.EnumerateObject())
        {
            if (port.Value.ValueKind == JsonValueKind.Null) continue;
            if (port.Name != bindingName) throw new JsonException("Unexpected published port.");
            var binding = SingleBinding(port.Value);
            RequireAddress(binding, address);
            hostPort = ParseHostPort(binding);
        }
        return hostPort;
    }

    public static string GetPrivateRuntimeHostAddress()
    {
        var configured = Environment.GetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Application publication requires an explicitly configured runtime host address.");
        if (!IPAddress.TryParse(configured, out var address))
            throw new InvalidOperationException("Application publication requires a concrete runtime host address.");
        var bytes = address.GetAddressBytes();
        if ((IPAddress.IsLoopback(address) && string.Equals(Environment.GetEnvironmentVariable("AUTOMATON_LOCAL_DOCKER_TEST"), "true", StringComparison.Ordinal))
            || address.IsIPv6UniqueLocal
            || bytes.Length == 4 && (bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168))
            return address.ToString();
        throw new InvalidOperationException("Application publication requires a trusted private runtime host address.");
    }

    private static JsonElement SingleBinding(JsonElement bindings)
    {
        if (bindings.ValueKind != JsonValueKind.Array || bindings.GetArrayLength() != 1)
            throw new JsonException("Expected exactly one host binding.");
        return bindings[0];
    }

    private static void RequireAddress(JsonElement binding, string expected)
    {
        if (!IPAddress.TryParse(binding.GetProperty("HostIp").GetString(), out var address)
            || !address.Equals(IPAddress.Parse(expected)))
            throw new JsonException("Unexpected host binding address.");
    }

    private static int? ParseHostPort(JsonElement binding)
    {
        var value = binding.GetProperty("HostPort").GetString();
        if (value is "" or "0") return null;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65_535)
            throw new JsonException("Invalid host port.");
        return port;
    }

    private static bool TryParseTcpPort(string value, out int port)
    {
        port = 0;
        return value.EndsWith("/tcp", StringComparison.Ordinal)
            && int.TryParse(value[..^4], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is >= 1 and <= 65_535;
    }

}
