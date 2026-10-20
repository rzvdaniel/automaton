using System.Globalization;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class MachineStaticServingContainer(
    Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> execute,
    MachineStaticServingOptions options)
{
    private static readonly TimeSpan DockerTimeout = TimeSpan.FromMinutes(5);

    public MachineStaticServingContainer(
        InfrastructureCommandService infrastructureCommandService,
        MachineStaticServingOptions options)
        : this((arguments, timeout, cancellationToken) =>
            infrastructureCommandService.ExecuteTypedDockerAsync(arguments, timeout, cancellationToken), options)
    {
    }

    public async Task<MachineStaticServerEndpoint?> EnsureAsync(
        HostContainerScope scope,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(scope) || !options.IsValid()) return null;
        if (!await HostContainerResourcePolicy.EnsureVolumeAsync(scope, execute, cancellationToken)) return null;

        var inspection = await InspectAsync(scope, cancellationToken);
        if (inspection is null)
        {
            var pull = await execute(["docker", "pull", options.Image], DockerTimeout, cancellationToken);
            if (!pull.Success) return null;
            var run = await execute(BuildRunArguments(scope), DockerTimeout, cancellationToken);
            if (!run.Success) return null;
            inspection = await InspectAsync(scope, cancellationToken);
        }
        else if (!inspection.Running)
        {
            var start = await execute(["docker", "start", ContainerName()], DockerTimeout, cancellationToken);
            if (!start.Success) return null;
            inspection = await InspectAsync(scope, cancellationToken);
        }
        return inspection?.Endpoint;
    }

    public async Task<bool> ReloadAsync(HostContainerScope scope, CancellationToken cancellationToken)
    {
        if (!IsAuthorized(scope)) return false;
        var inspection = await InspectAsync(scope, cancellationToken);
        if (inspection is null || !inspection.Running) return true;

        var test = await execute(["docker", "exec", ContainerName(), "nginx", "-t"], DockerTimeout, cancellationToken);
        if (!test.Success) return false;
        var reload = await execute(["docker", "exec", ContainerName(), "nginx", "-s", "reload"], DockerTimeout, cancellationToken);
        return reload.Success;
    }

    private async Task<ContainerInspection?> InspectAsync(
        HostContainerScope scope,
        CancellationToken cancellationToken)
    {
        var result = await execute(
            ["docker", "container", "inspect", "--format", "{{json .}}", ContainerName()],
            DockerTimeout,
            cancellationToken);
        if (!result.Success)
        {
            if (IsMissingContainer(result.Error)) return null;
            throw new InvalidOperationException("The Machine Management Runner could not inspect its static-serving container.");
        }

        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(
            root.GetProperty("Config").GetProperty("Labels")) ?? [];
        var expected = ExpectedLabels(scope);
        if (labels.Count != expected.Count || expected.Any(pair => !labels.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new UnauthorizedAccessException("The existing static-serving container is not owned by this Machine Management Runner.");

        var running = root.GetProperty("State").GetProperty("Running").GetBoolean();
        return new ContainerInspection(running, running ? ReadEndpoint(root, options.ContainerPort) : null);
    }

    private IReadOnlyList<string> BuildRunArguments(HostContainerScope scope)
    {
        var installationId = RequireInstallationId();
        var volume = HostContainerResourcePolicy.VolumeName(scope);
        var limits = new HostContainerResourceLimits(options.CpuLimitCores, options.MemoryLimitMb, options.PidsLimit);
        var arguments = new List<string>
        {
            "docker", "run", "--detach", "--name", ContainerName(), "--restart", "unless-stopped",
            "--label", "automaton.managed=true",
            "--label", $"automaton.machine-runner-id={scope.MachineRunnerId:D}",
            "--label", $"automaton.installation-id={installationId}",
            "--label", "automaton.resource=machine-static-server",
            "--read-only",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=16m",
            "--tmpfs", "/var/cache/nginx:rw,nosuid,nodev,size=32m",
            "--tmpfs", "/var/run:rw,nosuid,nodev,size=1m",
            "--log-driver", "local",
            "--log-opt", $"max-size={options.DockerLogMaxSizeMb}m",
            "--log-opt", $"max-file={options.DockerLogMaxFiles}",
            "--cpus", limits.CpuLimitCores.ToString(CultureInfo.InvariantCulture),
            "--memory", $"{limits.MemoryLimitMb}m",
            "--pids-limit", limits.PidsLimit.ToString(CultureInfo.InvariantCulture),
            "--mount", VolumeMount(volume, "workspace/published-static/sites", "/sites"),
            "--mount", VolumeMount(volume, "workspace/published-static/metadata/nginx", "/etc/nginx/conf.d"),
            "--publish", $"127.0.0.1::{options.ContainerPort}/tcp",
            options.Image
        };
        return arguments;
    }

    private static bool IsAuthorized(HostContainerScope scope)
        => HostContainerRunnerAuthorization.Validate() is null
            && HostContainerRunnerAuthorization.ValidateScope(
                new HostContainerExecutionRequest { Scope = scope }) is null
            && scope.MachineRunnerId != Guid.Empty;

    private static Dictionary<string, string> ExpectedLabels(HostContainerScope scope)
        => new(StringComparer.Ordinal)
        {
            ["automaton.managed"] = "true",
            ["automaton.machine-runner-id"] = scope.MachineRunnerId.ToString("D"),
            ["automaton.installation-id"] = RequireInstallationId(),
            ["automaton.resource"] = "machine-static-server"
        };

    private static string ContainerName() => $"automaton-static-{RequireInstallationId()[3..]}";

    private static string RequireInstallationId()
    {
        var value = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID");
        if (value is not { Length: 35 } || !value.StartsWith("sh_", StringComparison.Ordinal)
            || !Guid.TryParseExact(value.AsSpan(3), "N", out _))
            throw new InvalidOperationException("The Machine Management Runner installation identity is unavailable.");
        return value;
    }

    private static string VolumeMount(string volume, string subpath, string target)
        => string.Join(',', new[]
        {
            "type=volume", $"source={volume}", $"target={target}", $"volume-subpath={subpath}", "volume-nocopy", "readonly"
        }.Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static MachineStaticServerEndpoint ReadEndpoint(JsonElement root, int containerPort)
    {
        var bindings = root.GetProperty("NetworkSettings").GetProperty("Ports")
            .GetProperty($"{containerPort}/tcp");
        if (bindings.ValueKind != JsonValueKind.Array || bindings.GetArrayLength() != 1)
            throw new InvalidDataException("Docker did not publish exactly one static-server endpoint.");
        var binding = bindings[0];
        var host = binding.GetProperty("HostIp").GetString();
        if (host != "127.0.0.1" || !int.TryParse(binding.GetProperty("HostPort").GetString(), out var port)
            || port is < 1 or > 65_535)
            throw new InvalidDataException("Docker returned an invalid loopback static-server endpoint.");
        return new MachineStaticServerEndpoint(host, port);
    }

    private static bool IsMissingContainer(string error)
        => error.Contains("No such container", StringComparison.OrdinalIgnoreCase)
            || error.Contains("No such object", StringComparison.OrdinalIgnoreCase);

    private sealed record ContainerInspection(bool Running, MachineStaticServerEndpoint? Endpoint);
}
