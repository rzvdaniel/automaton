using System.Globalization;
using System.Net;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed partial class HostContainerExecutionService
{
    internal const int MaximumLogCharacters = 65_536;
    private static readonly TimeSpan RuntimeOperationTimeout = TimeSpan.FromSeconds(30);

    private async Task<HostContainerExecutionResponse> CreateApplicationContainerAsync(
        HostContainerExecutionRequest request, CancellationToken cancellationToken)
    {
        var scope = request.Scope;
        var volume = $"automaton-hosted-runner-{scope.HostedRunnerId:N}-user";
        var volumeInspection = await executor(
            ["docker", "volume", "inspect", "--format", "{{json .Labels}}", volume],
            RuntimeOperationTimeout, cancellationToken);
        if (!volumeInspection.Success || !HasExpectedVolumeLabels(volumeInspection.Output, scope.HostedRunnerId))
            return Failure("trusted_storage", "The Hosted Runner user volume is missing or has invalid ownership labels.");

        string hostAddress;
        try { hostAddress = GetPrivateRuntimeHostAddress(); }
        catch (InvalidOperationException ex) { return Failure("runtime_address", ex.Message); }
        var name = $"automaton-app-{scope.AppId:N}-{scope.DeploymentTargetId:N}-{request.CandidateId:N}";
        var args = new List<string>
        {
            "docker", "create", "--name", name, "--network", "bridge",
            "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
            "--publish", $"{FormatPublishAddress(hostAddress)}::{request.ContainerPort}",
            "--mount", VolumeMount(volume,
                RunnerWorkspaceService.GetRuntimeCandidateVolumeSubpath(scope.AppId, scope.DeploymentTargetId, request.CandidateId),
                "/automaton/app", readOnly: true),
            "--mount", VolumeMount(volume,
                RunnerWorkspaceService.GetApplicationDataVolumeSubpath(scope.AppId, request.Storage!.DataDirectory),
                "/automaton/data", readOnly: false)
        };
        foreach (var label in RuntimeLabels(scope))
        {
            args.Add("--label");
            args.Add($"{label.Key}={label.Value}");
        }
        var environment = new Dictionary<string, string>(request.Command!.Environment, StringComparer.Ordinal);
        foreach (var pair in request.Environment)
            environment[pair.Key] = pair.Value;
        environment["AUTOMATON_PORT"] = request.ContainerPort.ToString(CultureInfo.InvariantCulture);
        environment["AUTOMATON_APP_DATA_DIR"] = "/automaton/data";
        environment["AUTOMATON_APP_ID"] = scope.AppId.ToString();
        environment["AUTOMATON_DEPLOYMENT_TARGET_ID"] = scope.DeploymentTargetId.ToString();
        foreach (var mount in request.Storage.Mounts)
        {
            args.Add("--mount");
            args.Add(VolumeMount(volume,
                RunnerWorkspaceService.GetApplicationDataVolumeSubpath(scope.AppId, mount.RelativePath),
                $"/automaton/data/{mount.Name}", readOnly: false));
            environment[$"AUTOMATON_STORAGE_{mount.Name.ToUpperInvariant()}"] = $"/automaton/data/{mount.Name}";
        }
        try
        {
            args.Add("--workdir");
            args.Add(DockerContainerRuntime.ResolveContainerWorkingDirectory(request.Command.WorkingDirectory, "/automaton/app"));
            DockerContainerRuntime.AddEnvironment(args, environment);
            DockerContainerRuntime.AddEntrypoint(args, request.Command, "/automaton/app");
            args.Add(request.RequestedImage!);
            DockerContainerRuntime.AddCommandArguments(args, request.Command);
        }
        catch (InvalidOperationException ex) { return Failure("command", ex.Message); }

        var created = await executor(args, RuntimeOperationTimeout, cancellationToken);
        if (!created.Success)
            return Failure("container_create", "Docker could not create the application container.") with { ContainerName = name };
        var id = created.Output.Trim();
        if (!HostContainerRuntimeValidation.IsContainerId(id))
            return Failure("container_create", "Docker did not return a full container ID.") with { ContainerName = name };
        return new HostContainerExecutionResponse
        {
            Success = true, Exists = true, ContainerId = id, ContainerName = name,
            RequestedImage = request.RequestedImage!, ResolvedImage = request.RequestedImage!,
            Labels = RuntimeLabels(scope)
        };
    }

    private async Task<HostContainerExecutionResponse> ExecuteContainerOperationAsync(
        HostContainerExecutionRequest request, CancellationToken cancellationToken)
    {
        var inspection = await InspectOwnedContainerAsync(request.ContainerId!, request.Scope, cancellationToken,
            discoverEndpoint: request.Operation is HostContainerOperationKinds.InspectContainer or HostContainerOperationKinds.StartContainer);
        if (!inspection.Success || request.Operation == HostContainerOperationKinds.InspectContainer)
            return inspection;
        if (!inspection.Exists)
            return request.Operation == HostContainerOperationKinds.RemoveContainer
                ? inspection : Failure("container_missing", "The application container does not exist.");

        var arguments = request.Operation switch
        {
            HostContainerOperationKinds.StartContainer => new[] { "docker", "start", inspection.ContainerId },
            HostContainerOperationKinds.GetContainerLogs => new[] { "docker", "logs", "--tail", "200", inspection.ContainerId },
            HostContainerOperationKinds.RemoveContainer => new[] { "docker", "rm", "-f", inspection.ContainerId },
            _ => throw new InvalidOperationException("Unsupported container operation.")
        };
        var result = await executor(arguments, RuntimeOperationTimeout, cancellationToken);
        if (!result.Success)
        {
            if (request.Operation == HostContainerOperationKinds.RemoveContainer && IsMissingContainer(result, inspection.ContainerId))
                return new HostContainerExecutionResponse { Success = true };
            return Failure("container_operation", "Docker could not complete the application container operation.");
        }
        if (request.Operation == HostContainerOperationKinds.GetContainerLogs)
        {
            var logs = result.Output + result.Error;
            return inspection with { Logs = logs.Length <= MaximumLogCharacters ? logs : logs[^MaximumLogCharacters..] };
        }
        if (request.Operation == HostContainerOperationKinds.RemoveContainer)
            return new HostContainerExecutionResponse { Success = true, ContainerId = inspection.ContainerId };
        var postStart = await InspectOwnedContainerAsync(request.ContainerId!, request.Scope, cancellationToken, discoverEndpoint: true);
        if (!postStart.Success || !postStart.Exists || !postStart.Running || !postStart.HostPort.HasValue)
            return Failure("container_start_endpoint", "The application container started without a valid running private endpoint.") with
            {
                ContainerId = inspection.ContainerId,
                ContainerName = inspection.ContainerName,
                Exists = postStart.Exists,
                Running = postStart.Running,
                Labels = postStart.Labels
            };
        return postStart;
    }

    private async Task<HostContainerExecutionResponse> InspectOwnedContainerAsync(
        string reference, HostContainerScope scope, CancellationToken cancellationToken, bool discoverEndpoint)
    {
        var result = await executor(["docker", "container", "inspect", "--format", "{{json .}}", reference],
            RuntimeOperationTimeout, cancellationToken);
        if (!result.Success)
            return IsMissingContainer(result, reference)
                ? new HostContainerExecutionResponse { Success = true }
                : Failure("container_inspect", "Docker could not inspect the application container.");
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            RejectDuplicateProperties(root);
            var id = root.GetProperty("Id").GetString();
            if (!HostContainerRuntimeValidation.IsContainerId(id))
                throw new JsonException("Invalid container ID.");
            var running = root.GetProperty("State").GetProperty("Running").GetBoolean();
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(root.GetProperty("Config").GetProperty("Labels"))
                ?? throw new JsonException("Missing labels.");
            if (labels.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
                throw new JsonException("Invalid labels.");
            if (RuntimeLabels(scope).Any(pair => !labels.TryGetValue(pair.Key, out var value) || value != pair.Value))
                return Failure("container_ownership", "The container is not owned by the requested application and Hosted Runner scope.");

            if (!discoverEndpoint)
                return new HostContainerExecutionResponse
                {
                    Success = true, Exists = true, Running = running, ContainerId = id!, Labels = labels
                };

            var address = GetPrivateRuntimeHostAddress();
            var hostConfig = root.GetProperty("HostConfig");
            if (hostConfig.GetProperty("NetworkMode").GetString() != "bridge")
                throw new JsonException("Unexpected runtime network.");
            var configured = hostConfig.GetProperty("PortBindings").EnumerateObject().ToArray();
            if (configured.Length != 1 || !TryParseTcpPort(configured[0].Name, out var containerPort))
                throw new JsonException("Expected one TCP publication.");
            var requestedBinding = SingleBinding(configured[0].Value);
            RequireAddress(requestedBinding, address);
            ParseHostPort(requestedBinding);

            int? hostPort = null;
            var ports = root.GetProperty("NetworkSettings").GetProperty("Ports");
            if (ports.ValueKind != JsonValueKind.Null)
            {
                foreach (var port in ports.EnumerateObject())
                {
                    if (port.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    if (port.Name != configured[0].Name)
                        throw new JsonException("Unexpected published port.");
                    var binding = SingleBinding(port.Value);
                    RequireAddress(binding, address);
                    hostPort = ParseHostPort(binding);
                }
            }
            if (running && !hostPort.HasValue)
                throw new JsonException("Running container has no effective publication.");
            return new HostContainerExecutionResponse
            {
                Success = true, Exists = true, Running = running, ContainerId = id!, Labels = labels,
                ContainerPort = containerPort, HostPort = hostPort, HostAddress = hostPort.HasValue ? address : null
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Failure("container_inspect", "Docker returned invalid application container state or endpoint configuration.");
        }
    }

    private static Dictionary<string, string> RuntimeLabels(HostContainerScope scope) => new(StringComparer.Ordinal)
    {
        ["com.automaton.managed"] = "true",
        ["com.automaton.runtime-kind"] = "generic",
        ["com.automaton.app-id"] = scope.AppId.ToString(),
        ["com.automaton.deployment-target-id"] = scope.DeploymentTargetId.ToString(),
        ["com.automaton.hosted-runner-id"] = scope.HostedRunnerId.ToString(),
        ["com.automaton.runner-host-id"] = scope.RunnerHostId.ToString()
    };

    private static string VolumeMount(string volume, string subpath, string target, bool readOnly)
        => string.Join(',', new[] { "type=volume", $"source={volume}", $"target={target}", $"volume-subpath={subpath}", "volume-nocopy" }
            .Concat(readOnly ? new[] { "readonly" } : [])
            .Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static string GetPrivateRuntimeHostAddress()
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

    private static string FormatPublishAddress(string address) => address.Contains(':') ? $"[{address}]" : address;

    private static bool IsMissingContainer(InfrastructureCommandResponse result, string reference)
        => result.Error.Trim() == $"Error response from daemon: No such container: {reference}"
            || result.Error.Trim() == $"Error: No such container: {reference}"
            || result.Error.Trim() == $"Error: No such object: {reference}"
            || result.Error.Trim() == $"Error response from daemon: No such object: {reference}";

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

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
