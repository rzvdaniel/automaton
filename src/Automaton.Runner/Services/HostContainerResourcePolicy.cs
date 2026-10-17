using System.Text.Json;
using System.Globalization;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class HostContainerResourcePolicy
{
    private static readonly TimeSpan VolumeOperationTimeout = TimeSpan.FromSeconds(30);

    public static string VolumeName(HostContainerScope scope)
        => scope.MachineRunnerId != Guid.Empty
            ? $"automaton-machine-runner-{RequireInstallationId()}-workspace"
            : $"automaton-hosted-runner-{scope.HostedRunnerId:N}-user";

    public static bool HasExpectedVolumeLabels(string json, HostContainerScope scope)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (HasDuplicateProperties(document.RootElement)) return false;
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(document.RootElement);
            return scope.MachineRunnerId != Guid.Empty
                ? HasMachineVolumeLabels(labels, RequireInstallationId())
                : HasHostedVolumeLabels(labels, scope.HostedRunnerId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return true;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static bool HasMachineVolumeLabels(Dictionary<string, string>? labels, string installationId)
        => labels is { Count: 4 }
            && HasManagedWorkspaceLabels(labels, "workspace")
            && labels.TryGetValue("automaton.resource", out var resource)
            && resource == "machine-workspace-volume"
            && labels.TryGetValue("automaton.installation-id", out var installation)
            && installation == installationId;

    private static bool HasHostedVolumeLabels(Dictionary<string, string>? labels, Guid runnerId)
        => labels is { Count: 4 }
            && HasManagedWorkspaceLabels(labels, "user")
            && labels.TryGetValue("automaton.resource", out var resource)
            && resource == "hosted-runner-volume"
            && labels.TryGetValue("automaton.hosted-runner-id", out var hosted)
            && hosted == runnerId.ToString();

    private static bool HasManagedWorkspaceLabels(Dictionary<string, string> labels, string expectedScope)
        => labels.TryGetValue("automaton.managed", out var managed)
            && managed == "true"
            && labels.TryGetValue("automaton.storage-scope", out var storageScope)
            && storageScope == expectedScope;

    private static string RequireInstallationId()
    {
        var installationId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID");
        if (!IsValidInstallationId(installationId))
            throw new InvalidOperationException("The Machine Management Runner installation identity is unavailable.");
        return installationId!;
    }

    private static bool IsValidInstallationId(string? value)
        => value is { Length: 35 }
            && value.StartsWith("sh_", StringComparison.Ordinal)
            && Guid.TryParseExact(value.AsSpan(3), "N", out _);

    public static async Task<bool> EnsureVolumeAsync(
        HostContainerScope scope,
        Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> execute,
        CancellationToken cancellationToken)
    {
        var volume = VolumeName(scope);
        var inspection = await execute(
            ["docker", "volume", "inspect", "--format", "{{json .Labels}}", volume],
            VolumeOperationTimeout, cancellationToken);
        if (inspection.Success)
            return HasExpectedVolumeLabels(inspection.Output, scope);
        return false;
    }

    public static void AddCgroupParent(List<string> arguments, HostContainerScope scope)
    {
        if (scope.MachineRunnerId != Guid.Empty || scope.HostedRunnerId == Guid.Empty) return;
        arguments.Add("--cgroup-parent");
        arguments.Add(HostedRunnerResourceGroup.GetCgroupParent(scope.HostedRunnerId));
    }

    public static void AddContainerLimits(List<string> arguments, HostContainerResourceLimits? limits)
    {
        if (limits is null) return;
        if (!limits.IsValid())
            throw new InvalidOperationException("The server supplied invalid container resource limits.");
        arguments.Add("--cpus");
        arguments.Add(limits.CpuLimitCores.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--memory");
        arguments.Add($"{limits.MemoryLimitMb}m");
        arguments.Add("--pids-limit");
        arguments.Add(limits.PidsLimit.ToString(CultureInfo.InvariantCulture));
    }

}
