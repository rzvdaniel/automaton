using System.Net;
using System.Globalization;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class HostContainerComposeProjectExecutor(
    Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> executor)
{
    private static readonly TimeSpan ComposeOperationTimeout = TimeSpan.FromMinutes(15);

    public Task<HostContainerExecutionResponse> ExecuteAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
        => request.Operation switch
        {
            HostContainerOperationKinds.ApplyComposeProject => ApplyComposeProjectAsync(request, cancellationToken),
            HostContainerOperationKinds.InspectComposeProject => InspectComposeProjectAsync(request, cancellationToken),
            HostContainerOperationKinds.RemoveComposeProject => RemoveComposeProjectAsync(request, cancellationToken),
            _ => Task.FromResult(Failure("operation", "The Compose project operation is not supported."))
        };

    private async Task<HostContainerExecutionResponse> InspectComposeProjectAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var project = await FindExistingComposeProjectAsync(request.Scope, request.CandidateId,
            request.Compose!, HostContainerRuntimeEndpoint.GetPrivateRuntimeHostAddress(), cancellationToken);
        return project ?? new HostContainerExecutionResponse { Success = true, Exists = false };
    }

    private async Task<HostContainerExecutionResponse> ApplyComposeProjectAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var compose = request.Compose!;
        var scope = request.Scope;
        var hostAddress = HostContainerRuntimeEndpoint.GetPrivateRuntimeHostAddress();
        var limits = DivideLimits(request.ContainerLimits!, compose.Services.Count);
        if (limits is null)
            return Failure("compose_resource_policy", "The configured workload bounds are too small for every Compose service.");

        var project = HostContainerComposeDocumentBuilder.ProjectName(scope, request.CandidateId);
        var existing = await FindExistingComposeProjectAsync(scope, request.CandidateId, compose, hostAddress, cancellationToken);
        if (existing is not null) return existing;

        var resolvedImages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var service in compose.Services)
        {
            var resolved = await ResolveComposeImageAsync(service.Image, cancellationToken);
            if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.ResolvedImage)) return resolved;
            resolvedImages.Add(service.Name, resolved.ResolvedImage);
        }

        foreach (var volume in compose.Volumes)
        {
            if (!await EnsureComposeVolumeAsync(scope, volume, cancellationToken))
                return Failure("compose_volume", "A managed Compose volume has invalid ownership or could not be prepared.");
        }

        var tempDirectory = Directory.CreateTempSubdirectory("automaton-compose-");
        try
        {
            var file = Path.Combine(tempDirectory.FullName, "compose.yaml");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(HostContainerComposeDocumentBuilder.Build(
                compose, resolvedImages, scope, request.CandidateId, hostAddress, limits,
                request.SharedMemoryMb!.Value, request.DockerLogMaxSizeMb!.Value, request.DockerLogMaxFiles!.Value)), cancellationToken);
            var up = await executor(
                ["docker", "compose", "--project-name", project, "--file", file, "up", "--detach", "--pull", "never"],
                ComposeOperationTimeout, cancellationToken);
            if (!up.Success)
                return Failure("compose_start", "Docker Compose could not start the validated application services.");
            return await FindRunningPublicServiceAsync(scope, request.CandidateId, compose, hostAddress, cancellationToken);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private async Task<HostContainerExecutionResponse?> FindExistingComposeProjectAsync(
        HostContainerScope scope,
        Guid candidateId,
        ApplicationComposeSpec compose,
        string hostAddress,
        CancellationToken cancellationToken)
    {
        var ids = await FindComposeContainerIdsAsync(candidateId, cancellationToken);
        if (ids.Count == 0) return null;
        if (ids.Count != compose.Services.Count)
            return Failure("compose_incomplete", "A partial Compose candidate already exists; it was preserved without re-executing service commands.");

        HostContainerExecutionResponse? publicResult = null;
        foreach (var id in ids)
        {
            var inspected = await InspectComposeContainerAsync(id, scope, candidateId, hostAddress, cancellationToken);
            if (!inspected.Success || !inspected.Running)
                return Failure("compose_incomplete", "An existing Compose candidate is incomplete; it was preserved without re-executing service commands.");
            if (inspected.Labels!.TryGetValue("com.docker.compose.service", out var service)
                && string.Equals(service, compose.PublicService, StringComparison.Ordinal))
                publicResult = inspected;
        }
        return publicResult ?? Failure("compose_public_service", "The running Compose candidate has no identified public service.");
    }

    private async Task<HostContainerExecutionResponse> FindRunningPublicServiceAsync(
        HostContainerScope scope,
        Guid candidateId,
        ApplicationComposeSpec compose,
        string hostAddress,
        CancellationToken cancellationToken)
    {
        var ids = await FindComposeContainerIdsAsync(candidateId, cancellationToken);
        if (ids.Count != compose.Services.Count)
            return Failure("compose_start", "Docker Compose did not create every declared service.");
        HostContainerExecutionResponse? result = null;
        foreach (var id in ids)
        {
            var inspected = await InspectComposeContainerAsync(id, scope, candidateId, hostAddress, cancellationToken);
            if (!inspected.Success || !inspected.Running)
                return Failure("compose_start", "A Docker Compose service did not reach the running state.");
            if (inspected.Labels!.TryGetValue("com.docker.compose.service", out var name)
                && string.Equals(name, compose.PublicService, StringComparison.Ordinal))
                result = inspected;
        }
        return result ?? Failure("compose_public_service", "Docker Compose did not create the routed service.");
    }

    private async Task<List<string>> FindComposeContainerIdsAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var filter = HostContainerComposeDocumentBuilder.CandidateLabel(candidateId);
        var result = await executor(
            ["docker", "ps", "--all", "--quiet", "--filter", $"label={filter}"],
            ComposeOperationTimeout, cancellationToken);
        if (!result.Success) throw new InvalidOperationException("Docker could not inspect the Compose candidate.");
        return result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private async Task<HostContainerExecutionResponse> InspectComposeContainerAsync(
        string id,
        HostContainerScope scope,
        Guid candidateId,
        string hostAddress,
        CancellationToken cancellationToken)
    {
        var result = await executor(["docker", "container", "inspect", "--format", "{{json .}}", id], ComposeOperationTimeout, cancellationToken);
        if (!result.Success) return Failure("compose_inspect", "Docker could not inspect a Compose service.");
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            RejectDuplicateProperties(document.RootElement);
            var root = document.RootElement;
            var identity = ReadComposeContainerIdentity(root, scope);
            if (identity.Failure is not null) return identity.Failure;
            var labels = identity.Labels!;
            if (!labels.TryGetValue("com.automaton.compose-candidate", out var candidate)
                || candidate != candidateId.ToString("N"))
                return Failure("compose_ownership", "A Compose service does not belong to the requested deployment candidate.");
            return ReadComposeEndpoint(root, identity.Id!, identity.Running, labels, hostAddress);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Failure("compose_inspect", "Docker returned invalid Compose service state.");
        }
    }

    private async Task<HostContainerExecutionResponse> ResolveComposeImageAsync(
        string image,
        CancellationToken cancellationToken)
    {
        var pulled = await executor(["docker", "pull", image], ComposeOperationTimeout, cancellationToken);
        if (!pulled.Success) return Failure("compose_image_pull", "Docker could not pull a Compose service image.");
        var inspected = await executor(
            ["docker", "image", "inspect", "--format", "{{index .RepoDigests 0}}", image],
            ComposeOperationTimeout, cancellationToken);
        var digest = inspected.Output.Trim();
        return inspected.Success && HostContainerRuntimeValidation.IsRepoDigest(digest)
            ? new HostContainerExecutionResponse { Success = true, RequestedImage = image, ResolvedImage = digest }
            : Failure("compose_image_digest", "Docker did not resolve a Compose image to an immutable repository digest.");
    }

    private static (string? Id, bool Running, Dictionary<string, string>? Labels, HostContainerExecutionResponse? Failure)
        ReadComposeContainerIdentity(JsonElement root, HostContainerScope scope)
    {
        var id = root.GetProperty("Id").GetString();
        if (!HostContainerRuntimeValidation.IsContainerId(id)) throw new JsonException("Invalid container ID.");
        var running = root.GetProperty("State").GetProperty("Running").GetBoolean();
        var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(root.GetProperty("Config").GetProperty("Labels"))
            ?? throw new JsonException("Missing labels.");
        var expected = ComposeRuntimeLabels(scope);
        return expected.Any(pair => !labels.TryGetValue(pair.Key, out var value) || value != pair.Value)
            ? (null, false, null, Failure("compose_ownership", "A Compose service is not owned by the requested application scope."))
            : (id, running, labels, null);
    }

    private static Dictionary<string, string> ComposeRuntimeLabels(HostContainerScope scope)
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

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };

    internal static HostContainerExecutionResponse ReadComposeEndpoint(
        JsonElement root,
        string id,
        bool running,
        Dictionary<string, string> labels,
        string expectedAddress)
    {
        var portBindings = root.GetProperty("HostConfig").GetProperty("PortBindings");
        if (portBindings.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new() { Success = true, Exists = true, Running = running, ContainerId = id, Labels = labels };
        if (portBindings.ValueKind != JsonValueKind.Object)
            return Failure("compose_ports", "A Compose service returned invalid port bindings.");
        var published = portBindings.EnumerateObject().ToArray();
        if (published.Length == 0)
            return new() { Success = true, Exists = true, Running = running, ContainerId = id, Labels = labels };
        if (published.Length != 1 || !published[0].Name.EndsWith("/tcp", StringComparison.Ordinal))
            return Failure("compose_ports", "A Compose service has an unexpected port publication.");
        if (!int.TryParse(published[0].Name[..^4], NumberStyles.None, CultureInfo.InvariantCulture, out var containerPort))
            return Failure("compose_ports", "A Compose service has an invalid published port.");
        var bindings = published[0].Value;
        if (bindings.ValueKind != JsonValueKind.Array || bindings.GetArrayLength() != 1)
            return Failure("compose_ports", "A Compose service must have exactly one private host binding.");
        var binding = bindings[0];
        var hostIp = binding.GetProperty("HostIp").GetString();
        var hostPort = binding.GetProperty("HostPort").GetString();
        if (!IPAddress.TryParse(hostIp, out var parsedAddress) || !parsedAddress.Equals(IPAddress.Parse(expectedAddress))
            || !int.TryParse(hostPort, NumberStyles.None, CultureInfo.InvariantCulture, out var effectivePort)
            || effectivePort is < 1 or > 65535)
            return Failure("compose_ports", "Docker returned an invalid private Compose endpoint.");
        return new()
        {
            Success = true, Exists = true, Running = running, ContainerId = id, Labels = labels,
            HostAddress = expectedAddress, HostPort = effectivePort, ContainerPort = containerPort,
            ResolvedImage = labels.GetValueOrDefault("com.automaton.resolved-image", string.Empty)
        };
    }

    private async Task<bool> EnsureComposeVolumeAsync(
        HostContainerScope scope,
        string volume,
        CancellationToken cancellationToken)
    {
        var name = HostContainerComposeDocumentBuilder.VolumeName(scope, volume);
        var inspect = await executor(["docker", "volume", "inspect", "--format", "{{json .Labels}}", name], ComposeOperationTimeout, cancellationToken);
        if (inspect.Success) return HasComposeVolumeLabels(inspect.Output, scope, volume);
        var created = await executor(ComposeVolumeCreateArguments(scope, volume, name), ComposeOperationTimeout, cancellationToken);
        if (!created.Success) return false;
        inspect = await executor(["docker", "volume", "inspect", "--format", "{{json .Labels}}", name], ComposeOperationTimeout, cancellationToken);
        return inspect.Success && HasComposeVolumeLabels(inspect.Output, scope, volume);
    }

    private static IReadOnlyList<string> ComposeVolumeCreateArguments(HostContainerScope scope, string volume, string name)
        => ["docker", "volume", "create", "--label", "com.automaton.managed=true",
            "--label", "com.automaton.resource=compose-app-volume",
            "--label", $"com.automaton.app-id={scope.AppId}",
            "--label", $"com.automaton.deployment-target-id={scope.DeploymentTargetId}",
            "--label", $"com.automaton.compose-volume={volume}", name];

    private async Task<HostContainerExecutionResponse> RemoveComposeProjectAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var candidateFilter = HostContainerComposeDocumentBuilder.CandidateLabel(request.CandidateId);
        var containersResult = await executor(["docker", "ps", "--all", "--quiet", "--filter", $"label={candidateFilter}"], ComposeOperationTimeout, cancellationToken);
        if (!containersResult.Success) return Failure("compose_cleanup", "Docker could not inspect Compose project ownership.");
        var ids = containersResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var id in ids)
        {
            var identity = await InspectComposeContainerAsync(id, request.Scope, request.CandidateId,
                HostContainerRuntimeEndpoint.GetPrivateRuntimeHostAddress(), cancellationToken);
            if (!identity.Success) return Failure("compose_cleanup", "A Compose container failed ownership verification and was preserved.");
        }
        if (ids.Length > 0)
        {
            var removed = await executor(new[] { "docker", "rm", "--force" }.Concat(ids).ToArray(), ComposeOperationTimeout, cancellationToken);
            if (!removed.Success) return Failure("compose_cleanup", "Docker could not remove the owned Compose containers.");
        }
        var networks = await executor(["docker", "network", "ls", "--quiet", "--filter", $"label={candidateFilter}"], ComposeOperationTimeout, cancellationToken);
        if (!networks.Success) return Failure("compose_cleanup", "Docker could not inspect Compose network ownership.");
        var networkIds = networks.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var networkId in networkIds)
        {
            var inspection = await executor(["docker", "network", "inspect", "--format", "{{json .Labels}}", networkId], ComposeOperationTimeout, cancellationToken);
            if (!inspection.Success || !HasComposeNetworkLabels(inspection.Output, request.Scope, request.CandidateId))
                return Failure("compose_cleanup", "A Compose network failed ownership verification and was preserved.");
            var removed = await executor(["docker", "network", "rm", networkId], ComposeOperationTimeout, cancellationToken);
            if (!removed.Success) return Failure("compose_cleanup", "Docker could not remove the owned Compose network.");
        }
        return new() { Success = true };
    }

    private static HostContainerResourceLimits? DivideLimits(HostContainerResourceLimits configured, int count)
    {
        var result = new HostContainerResourceLimits(
            decimal.Round(configured.CpuLimitCores / count, 2, MidpointRounding.ToZero),
            configured.MemoryLimitMb / count,
            configured.PidsLimit / count);
        return result.IsValid() ? result : null;
    }

    private static bool HasComposeNetworkLabels(string json, HostContainerScope scope, Guid candidateId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(document.RootElement);
            return labels is not null
                && labels.TryGetValue("com.automaton.managed", out var managed) && managed == "true"
                && labels.TryGetValue("com.automaton.app-id", out var appId) && appId == scope.AppId.ToString()
                && labels.TryGetValue("com.automaton.deployment-target-id", out var targetId) && targetId == scope.DeploymentTargetId.ToString()
                && labels.TryGetValue("com.automaton.compose-candidate", out var candidate) && candidate == candidateId.ToString("N");
        }
        catch (JsonException) { return false; }
    }

    private static bool HasComposeVolumeLabels(string json, HostContainerScope scope, string volume)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(document.RootElement);
            return labels is not null
                && labels.TryGetValue("com.automaton.managed", out var managed) && managed == "true"
                && labels.TryGetValue("com.automaton.resource", out var resource) && resource == "compose-app-volume"
                && labels.TryGetValue("com.automaton.app-id", out var appId) && appId == scope.AppId.ToString()
                && labels.TryGetValue("com.automaton.deployment-target-id", out var targetId) && targetId == scope.DeploymentTargetId.ToString()
                && labels.TryGetValue("com.automaton.compose-volume", out var name) && name == volume;
        }
        catch (JsonException) { return false; }
    }
}
