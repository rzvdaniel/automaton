using Automaton.Api.Entities;
using Automaton.Core.Models;
using System.Text.Json;

namespace Automaton.Api.Services;

public interface ITrustedSystemActionScheduleDispatchValidator
{
    Task<AppActionManifest> ValidateAsync(ActionSchedule schedule, CancellationToken cancellationToken);
}

public interface ITrustedSystemActionManifestSource
{
    AppActionManifest? Find(string actionKey);
    IReadOnlyList<AppActionManifest> List();
}

public sealed class TrustedSystemActionManifestSource : ITrustedSystemActionManifestSource
{
    public AppActionManifest? Find(string actionKey)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure");
        return AppActionManifestParser.Discover(root)
            .SingleOrDefault(x => x.Key == actionKey)?.Manifest;
    }

    public IReadOnlyList<AppActionManifest> List()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure");
        return AppActionManifestParser.Discover(root)
            .Select(x => x.Manifest).ToList();
    }
}

public sealed class TrustedSystemActionScheduleDispatchValidator(
    ITrustedSystemActionManifestSource manifests) : ITrustedSystemActionScheduleDispatchValidator
{
    public Task<AppActionManifest> ValidateAsync(ActionSchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (schedule.OwnerType != ActionScheduleOwnerTypes.System
            || !InfrastructureAppTrust.IsTrustedApp(schedule.ApplicationId))
            throw new InvalidOperationException("Worker schedules must belong to the trusted System Application.");

        var manifest = manifests.Find(schedule.ActionKey)
            ?? throw new KeyNotFoundException("Action not found in the trusted System Application.");
        if (!manifest.Spec.Schedulable)
            throw new InvalidOperationException("The Action manifest no longer allows scheduling.");
        if (!manifest.Spec.AllowedExecutionScopes.Contains(schedule.ExecutionScope, StringComparer.Ordinal))
            throw new InvalidOperationException("The Action manifest no longer allows the requested execution scope.");

        using var input = JsonDocument.Parse(schedule.InputJson);
        var schemaError = AppActionSchemaValidator.Validate(input.RootElement, manifest.Spec.Input);
        if (schemaError != null)
            throw new InvalidOperationException($"Action input is invalid: {schemaError}");
        return Task.FromResult(manifest);
    }
}
