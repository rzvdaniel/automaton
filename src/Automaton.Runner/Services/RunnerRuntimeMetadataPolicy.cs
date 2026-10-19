using Automaton.Core.Models;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

internal static class RunnerRuntimeMetadataPolicy
{
    public static bool HasComposeContract(ApplicationContract contract) => contract.Compose is not null;

    public static bool HasComposeMetadata(RunnerRuntimeDeploymentMetadata? metadata)
        => DeserializeCompose(metadata) is not null;

    public static bool IsStoppedContract(ApplicationContract contract)
        => contract.Runtime.Command is null && contract.Compose is null;

    public static string RuntimeImage(ApplicationContract contract)
        => contract.Compose is null
            ? contract.Runtime.Image!
            : contract.Compose.Services.Single(service => service.Name == contract.Compose.PublicService).Image;

    public static void ValidateApplyRequest(ApplicationContract contract, bool acceptingLifecycleWork)
    {
        if (!acceptingLifecycleWork)
            throw new InvalidOperationException("The managed runtime container manager is stopping and is not accepting new lifecycle work.");
        ApplicationContractValidator.EnsureDeployable(contract);
        var routedPort = contract.Compose is null
            ? contract.Runtime.Port
            : contract.Compose.Services.Single(service => service.Name == contract.Compose.PublicService).Port;
        if (contract.Health.Port is not null && contract.Health.Port != routedPort)
            throw new InvalidOperationException("health.port must match the routed service's container port.");
    }

    public static bool HasExpectedIdentity(DockerContainerIdentity identity, Guid appId, Guid targetId)
        => identity.Labels.TryGetValue("com.automaton.managed", out var managed)
            && string.Equals(managed, "true", StringComparison.OrdinalIgnoreCase)
            && identity.Labels.TryGetValue("com.automaton.app-id", out var actualApp)
            && string.Equals(actualApp, appId.ToString(), StringComparison.OrdinalIgnoreCase)
            && identity.Labels.TryGetValue("com.automaton.deployment-target-id", out var actualTarget)
            && string.Equals(actualTarget, targetId.ToString(), StringComparison.OrdinalIgnoreCase)
            && identity.Labels.TryGetValue("com.automaton.runtime-kind", out var kind)
            && string.Equals(kind, "generic", StringComparison.OrdinalIgnoreCase);

    public static bool IsOciContainer(RunnerRuntimeDeploymentMetadata? metadata)
        => metadata is not null
            && !string.IsNullOrWhiteSpace(metadata.ContainerId)
            && (!string.IsNullOrWhiteSpace(metadata.Runtime?.Image)
                || !string.IsNullOrWhiteSpace(metadata.ComposeJson) && !string.IsNullOrWhiteSpace(metadata.ComposeProjectId));

    public static bool IsOciCandidate(RunnerRuntimeDeploymentMetadata? metadata)
        => metadata is not null
            && (!string.IsNullOrWhiteSpace(metadata.ContainerId) || !string.IsNullOrWhiteSpace(metadata.ContainerName))
            && (!string.IsNullOrWhiteSpace(metadata.Runtime?.Image)
                || !string.IsNullOrWhiteSpace(metadata.ComposeJson) && !string.IsNullOrWhiteSpace(metadata.ComposeProjectId));

    public static ApplicationContract CreateApplicationContract(RunnerRuntimeDeploymentMetadata metadata)
        => new()
        {
            Runtime = CloneRuntimeWithResolvedImage(metadata.Runtime, metadata.ResolvedRuntimeImage),
            Health = metadata.Health ?? new ApplicationHealthSpec(),
            Storage = metadata.Storage ?? new ApplicationStorageSpec(),
            Compose = DeserializeCompose(metadata)
        };

    public static string? SerializeCompose(ApplicationComposeSpec? compose)
        => compose is null ? null : JsonSerializer.Serialize(compose);

    public static ApplicationComposeSpec? DeserializeCompose(RunnerRuntimeDeploymentMetadata metadata)
        => string.IsNullOrWhiteSpace(metadata.ComposeJson)
            ? null
            : JsonSerializer.Deserialize<ApplicationComposeSpec>(metadata.ComposeJson)
                ?? throw new InvalidDataException("Persisted Compose runtime metadata is invalid.");

    private static ApplicationRuntimeSpec CloneRuntimeWithResolvedImage(
        ApplicationRuntimeSpec? runtime,
        string? resolvedImage)
    {
        if (runtime is null)
            throw new InvalidOperationException("Persisted runtime metadata is incomplete.");
        return new ApplicationRuntimeSpec
        {
            Image = string.IsNullOrWhiteSpace(resolvedImage) ? runtime.Image : resolvedImage,
            User = runtime.User,
            Command = runtime.Command,
            Port = runtime.Port
        };
    }
}
