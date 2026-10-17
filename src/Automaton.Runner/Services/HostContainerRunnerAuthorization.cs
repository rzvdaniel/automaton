using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class HostContainerRunnerAuthorization
{
    public static HostContainerExecutionResponse? Validate()
    {
        var purpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        var capabilities = RunnerCapabilities.Normalize(
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            purpose);
        if (purpose == RunnerPurposes.Infrastructure)
            return capabilities.Contains(RunnerCapabilities.HostContainerExecution, StringComparer.OrdinalIgnoreCase)
                ? null
                : Failure("capability", "Host container execution is not enabled for this Infrastructure Runner.");

        var profile = RunnerApplicationProfiles.Normalize(
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"));
        return profile == RunnerApplicationProfiles.MachineManagement
            && capabilities.Contains(RunnerCapabilities.ManagedContainerExecution, StringComparer.OrdinalIgnoreCase)
                ? null
                : Failure("authorization", "Typed container execution is not enabled for this Runner profile.");
    }

    public static HostContainerExecutionResponse? ValidateScope(HostContainerExecutionRequest request)
    {
        var scope = request.Scope;
        if (scope is null || scope.AppId == Guid.Empty || scope.DeploymentTargetId == Guid.Empty)
            return Failure("scope", "A complete host-container scope is required.");

        return scope.MachineRunnerId != Guid.Empty
            ? ValidateMachineScope(scope)
            : scope.HostedRunnerId != Guid.Empty && scope.RunnerHostId != Guid.Empty
                ? null
                : Failure("scope", "A complete host-container scope is required.");
    }

    private static HostContainerExecutionResponse? ValidateMachineScope(HostContainerScope scope)
    {
        var configuredRunnerId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID");
        var installationId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID");
        return scope.HostedRunnerId == Guid.Empty && scope.RunnerHostId == Guid.Empty
            && Guid.TryParse(configuredRunnerId, out var runnerId) && runnerId == scope.MachineRunnerId
            && installationId is { Length: 35 }
            && installationId.StartsWith("sh_", StringComparison.Ordinal)
            && Guid.TryParseExact(installationId.AsSpan(3), "N", out _)
                ? null
                : Failure("scope", "Machine container scope does not match this Runner installation identity.");
    }

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };
}
