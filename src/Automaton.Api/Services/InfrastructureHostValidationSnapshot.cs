using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Api.Entities;
using Automaton.Api.Models;

namespace Automaton.Api.Services;

internal static class InfrastructureHostValidationSnapshot
{
    public static void Validate(JsonElement root)
    {
        foreach (var name in new[] { "totalCpuCores", "totalMemoryMb", "totalDiskMb" })
            if (root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Number)
                throw new ArgumentException($"Validation resource '{name}' must be numeric.");
        var health = root.GetProperty("health");
        foreach (var name in new[] { "docker", "network", "disk" })
            if (health.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"Validation health '{name}' must be a string.");
        if (!root.TryGetProperty("metadata", out var metadata)) return;
        if (metadata.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Validation metadata must be an object.");
        if (metadata.TryGetProperty("network", out var network) && network.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Validation network metadata must be an object.");
    }

    public static void Apply(RunnerHost host, string resultJson)
    {
        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;
        ApplyMetadataSnapshot(host, root);
        ApplyHealthSnapshot(host, root);
        ApplyNetworkSnapshot(host, root);
        ApplyResourceSnapshot(host, root);
        host.UpdatedAt = DateTime.UtcNow;
    }

    private static void ApplyMetadataSnapshot(RunnerHost host, JsonElement root)
    {
        if (root.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object)
            host.MetadataJson = metadata.GetRawText();
    }

    private static void ApplyHealthSnapshot(RunnerHost host, JsonElement root)
    {
        if (!root.TryGetProperty("health", out var health)
            || health.ValueKind != JsonValueKind.Object)
            return;

        host.HealthJson = health.GetRawText();
        var healthy = IsHealthCheckHealthy(health, "docker")
            && IsHealthCheckHealthy(health, "network")
            && IsHealthCheckHealthy(health, "disk");
        if (healthy)
        {
            MarkHostValidationSucceeded(host);
            return;
        }

        MarkHostValidationFailed(host, "One or more host validation checks failed.");
    }

    private static bool IsHealthCheckHealthy(JsonElement health, string checkName)
        => health.TryGetProperty(checkName, out var check)
            && string.Equals(check.GetString(), "Healthy", StringComparison.OrdinalIgnoreCase);

    private static void MarkHostValidationSucceeded(RunnerHost host)
    {
        var isLocalDevelopmentHost = host.ProvisioningTransport == RunnerHostProvisioningTransports.LocalDocker;
        var wasApproved = host.ProvisioningStatus == RunnerHostProvisioningStatuses.Ready
            && host.ApprovedAtUtc.HasValue;
        host.ProvisioningStatus = isLocalDevelopmentHost || wasApproved
            ? RunnerHostProvisioningStatuses.Ready
            : RunnerHostProvisioningStatuses.AwaitingApproval;
        host.ValidatedAtUtc = DateTime.UtcNow;
        host.ProvisioningFailureCode = null;
        host.ProvisioningFailureMessage = null;
        if (!isLocalDevelopmentHost && !wasApproved)
            host.HostedRunnerSchedulingEnabled = false;
    }

    private static void MarkHostValidationFailed(RunnerHost host, string message)
    {
        host.ProvisioningStatus = RunnerHostProvisioningStatuses.ValidationFailed;
        host.ValidatedAtUtc = null;
        host.ProvisioningFailureCode = "HostValidationFailed";
        host.ProvisioningFailureMessage = message;
        host.HostedRunnerSchedulingEnabled = false;
    }

    private static void ApplyNetworkSnapshot(RunnerHost host, JsonElement root)
    {
        if (root.TryGetProperty("metadata", out var metadataRoot)
            && metadataRoot.TryGetProperty("network", out var network)
            && network.TryGetProperty("privateIp", out var privateIp)
            && privateIp.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(privateIp.GetString())
            && !string.Equals(privateIp.GetString(), "unknown", StringComparison.OrdinalIgnoreCase))
        {
            host.PrivateIp = privateIp.GetString();
        }
    }

    private static void ApplyResourceSnapshot(RunnerHost host, JsonElement root)
    {
        if (TryGetDecimal(root, "totalCpuCores", out var totalCpuCores) && totalCpuCores > 0)
        {
            host.TotalCpuCores = totalCpuCores;
        }

        if (TryGetInt32(root, "totalMemoryMb", out var totalMemoryMb) && totalMemoryMb > 0)
        {
            host.TotalMemoryMb = totalMemoryMb;
        }

        if (TryGetInt32(root, "totalDiskMb", out var totalDiskMb) && totalDiskMb > 0)
        {
            host.TotalDiskMb = totalDiskMb;
        }
    }

    private static bool TryGetDecimal(JsonElement root, string propertyName, out decimal value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetDecimal(out value);
    }

    private static bool TryGetInt32(JsonElement root, string propertyName, out int value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
    }

}
