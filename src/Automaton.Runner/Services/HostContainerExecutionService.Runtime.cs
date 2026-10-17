using System.Globalization;
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
        var volume = HostContainerResourcePolicy.VolumeName(scope);
        if (!await HostContainerResourcePolicy.EnsureVolumeAsync(scope, executor, cancellationToken))
            return Failure("trusted_storage", "The app workspace volume is missing or has invalid ownership labels.");

        string hostAddress;
        try { hostAddress = HostContainerRuntimeEndpoint.GetPrivateRuntimeHostAddress(); }
        catch (InvalidOperationException ex) { return Failure("runtime_address", ex.Message); }
        var name = $"automaton-app-{scope.AppId:N}-{scope.DeploymentTargetId:N}-{request.CandidateId:N}";
        var existing = await InspectExistingApplicationCandidateAsync(request, name, cancellationToken);
        if (existing is not null) return existing;
        var resolved = await ResolveImageAsync(request, cancellationToken);
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.ResolvedImage))
            return resolved;
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
        HostContainerResourcePolicy.AddCgroupParent(args, scope);
        if (request.User is not null)
        {
            args.Add("--user");
            args.Add(request.User);
        }
        AddWritablePathArguments(
            args, request.SharedMemoryMb!.Value, request.DockerLogMaxSizeMb!.Value, request.DockerLogMaxFiles!.Value);
        foreach (var label in RuntimeLabels(scope))
        {
            args.Add("--label");
            args.Add($"{label.Key}={label.Value}");
        }
        args.Add("--label");
        args.Add($"automaton.requested-image={request.RequestedImage}");
        args.Add("--label");
        args.Add($"automaton.resolved-image={resolved.ResolvedImage}");
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
            args.Add(resolved.ResolvedImage);
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
            RequestedImage = request.RequestedImage!, ResolvedImage = resolved.ResolvedImage,
            Labels = RuntimeLabels(scope)
        };
    }

    private async Task<HostContainerExecutionResponse?> InspectExistingApplicationCandidateAsync(
        HostContainerExecutionRequest request,
        string name,
        CancellationToken cancellationToken)
    {
        var existing = await InspectOwnedContainerAsync(name, request.Scope, cancellationToken, discoverEndpoint: false);
        if (!existing.Success || !existing.Exists) return existing.Success ? null : existing;
        if (!existing.Labels!.TryGetValue("automaton.requested-image", out var requestedImage)
            || requestedImage != request.RequestedImage)
            return Failure("container_operation_conflict", "The existing candidate container was created from a different image request.");
        var resolvedImage = existing.Labels.TryGetValue("automaton.resolved-image", out var image) ? image : string.Empty;
        return existing with { RequestedImage = request.RequestedImage!, ResolvedImage = resolvedImage };
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
        if (!result.Success) return HandleFailedContainerOperation(request.Operation, result, inspection.ContainerId);
        return await CompleteContainerOperationAsync(request, result, inspection, cancellationToken);
    }

    private static HostContainerExecutionResponse HandleFailedContainerOperation(
        string operation,
        InfrastructureCommandResponse result,
        string containerId)
    {
        if (operation == HostContainerOperationKinds.RemoveContainer && IsMissingContainer(result, containerId))
            return new HostContainerExecutionResponse { Success = true };
        return Failure("container_operation", "Docker could not complete the application container operation.");
    }

    private async Task<HostContainerExecutionResponse> CompleteContainerOperationAsync(
        HostContainerExecutionRequest request,
        InfrastructureCommandResponse result,
        HostContainerExecutionResponse inspection,
        CancellationToken cancellationToken)
    {
        if (request.Operation == HostContainerOperationKinds.GetContainerLogs)
        {
            var logs = result.Output + result.Error;
            return inspection with { Logs = logs.Length <= MaximumLogCharacters ? logs : logs[^MaximumLogCharacters..] };
        }
        if (request.Operation == HostContainerOperationKinds.RemoveContainer)
            return new HostContainerExecutionResponse { Success = true, ContainerId = inspection.ContainerId };
        var postStart = await InspectOwnedContainerAsync(inspection.ContainerId, request.Scope, cancellationToken, discoverEndpoint: true);
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
            var inspection = ReadContainerIdentity(root, scope);
            if (inspection.Failure is not null) return inspection.Failure;
            if (!discoverEndpoint) return CreateInspectionResponse(inspection.Id!, inspection.Running, inspection.Labels!);
            return HostContainerRuntimeEndpoint.Read(root, inspection.Id!, inspection.Running, inspection.Labels!);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Failure("container_inspect", "Docker returned invalid application container state or endpoint configuration.");
        }
    }

    private static (string? Id, bool Running, Dictionary<string, string>? Labels, HostContainerExecutionResponse? Failure)
        ReadContainerIdentity(JsonElement root, HostContainerScope scope)
    {
        var id = root.GetProperty("Id").GetString();
        if (!HostContainerRuntimeValidation.IsContainerId(id)) throw new JsonException("Invalid container ID.");
        var running = root.GetProperty("State").GetProperty("Running").GetBoolean();
        var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(root.GetProperty("Config").GetProperty("Labels"))
            ?? throw new JsonException("Missing labels.");
        if (labels.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
            throw new JsonException("Invalid labels.");
        if (RuntimeLabels(scope).Any(pair => !labels.TryGetValue(pair.Key, out var value) || value != pair.Value))
            return (null, false, null, Failure("container_ownership", "The container is not owned by the requested application and Hosted Runner scope."));
        return (id, running, labels, null);
    }

    private static HostContainerExecutionResponse CreateInspectionResponse(
        string id,
        bool running,
        Dictionary<string, string> labels)
        => new() { Success = true, Exists = true, Running = running, ContainerId = id, Labels = labels };

    private static Dictionary<string, string> RuntimeLabels(HostContainerScope scope)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["com.automaton.managed"] = "true",
            ["com.automaton.runtime-kind"] = "generic",
            ["com.automaton.app-id"] = scope.AppId.ToString(),
            ["com.automaton.deployment-target-id"] = scope.DeploymentTargetId.ToString()
        };
        if (scope.MachineRunnerId != Guid.Empty)
            labels["com.automaton.machine-runner-id"] = scope.MachineRunnerId.ToString();
        else
        {
            labels["com.automaton.hosted-runner-id"] = scope.HostedRunnerId.ToString();
            labels["com.automaton.runner-host-id"] = scope.RunnerHostId.ToString();
        }
        return labels;
    }

    private static string VolumeMount(string volume, string subpath, string target, bool readOnly)
        => string.Join(',', new[] { "type=volume", $"source={volume}", $"target={target}", $"volume-subpath={subpath}", "volume-nocopy" }
            .Concat(readOnly ? new[] { "readonly" } : [])
            .Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static string FormatPublishAddress(string address) => address.Contains(':') ? $"[{address}]" : address;

    private static bool IsMissingContainer(InfrastructureCommandResponse result, string reference)
        => result.Error.Trim() == $"Error response from daemon: No such container: {reference}"
            || result.Error.Trim() == $"Error: No such container: {reference}"
            || result.Error.Trim() == $"Error: No such object: {reference}"
            || result.Error.Trim() == $"Error response from daemon: No such object: {reference}";

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
