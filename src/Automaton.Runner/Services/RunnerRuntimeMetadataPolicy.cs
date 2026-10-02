using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class RunnerRuntimeMetadataPolicy
{
    public static void ValidateApplyRequest(ApplicationContract contract, bool acceptingLifecycleWork)
    {
        if (!acceptingLifecycleWork)
            throw new InvalidOperationException("The managed runtime container manager is stopping and is not accepting new lifecycle work.");
        ApplicationContractValidator.EnsureDeployable(contract);
        if (contract.Health.Port is not null && contract.Health.Port != contract.Runtime.Port)
            throw new InvalidOperationException("A health.port different from runtime.port is not supported by the isolated application-container network boundary.");
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
            && !string.IsNullOrWhiteSpace(metadata.Runtime?.Image);

    public static bool IsOciCandidate(RunnerRuntimeDeploymentMetadata? metadata)
        => metadata is not null
            && (!string.IsNullOrWhiteSpace(metadata.ContainerId) || !string.IsNullOrWhiteSpace(metadata.ContainerName))
            && !string.IsNullOrWhiteSpace(metadata.Runtime?.Image);

    public static ApplicationContract CreateApplicationContract(RunnerRuntimeDeploymentMetadata metadata)
        => new()
        {
            Runtime = CloneRuntimeWithResolvedImage(metadata.Runtime, metadata.ResolvedRuntimeImage),
            Health = metadata.Health ?? new ApplicationHealthSpec(),
            Storage = metadata.Storage ?? new ApplicationStorageSpec()
        };

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
