using System.Text.Json;

namespace Automaton.Core.Enums;

/// <summary>
/// Declares the operations a Runner installation is prepared to perform.
/// Capabilities describe the deployment profile; they do not grant access on
/// their own. API authorization and the Runner installation still enforce the
/// security boundary.
/// </summary>
public static class RunnerCapabilities
{
    public const string ApplicationHosting = "ApplicationHosting";
    public const string WorkflowExecution = "WorkflowExecution";
    public const string ManagedContainerExecution = "ManagedContainerExecution";

    private static readonly HashSet<string> SupportedCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationHosting,
        WorkflowExecution,
        ManagedContainerExecution
    };

    public static IReadOnlyList<string> DefaultsForPurpose(string? purpose)
    {
        _ = RunnerPurposes.Normalize(purpose);
        return [ApplicationHosting, WorkflowExecution, ManagedContainerExecution];
    }

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? capabilities, string? purpose)
    {
        if (!string.Equals(purpose?.Trim(), RunnerPurposes.Managed, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var values = capabilities?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(SupportedCapabilities.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (values == null || values.Count == 0)
        {
            return DefaultsForPurpose(purpose);
        }

        return values;
    }

    public static string Serialize(IEnumerable<string>? capabilities, string? purpose)
        => JsonSerializer.Serialize(Normalize(capabilities, purpose));

    public static IReadOnlyList<string> Deserialize(string? capabilitiesJson, string? purpose)
    {
        if (string.IsNullOrWhiteSpace(capabilitiesJson))
        {
            return DefaultsForPurpose(purpose);
        }

        try
        {
            var values = JsonSerializer.Deserialize<string[]>(capabilitiesJson);
            return Normalize(values, purpose);
        }
        catch (JsonException)
        {
            return DefaultsForPurpose(purpose);
        }
    }

    public static bool Includes(string? capabilitiesJson, string capability, string? purpose = null)
        => Deserialize(capabilitiesJson, purpose)
            .Contains(capability, StringComparer.OrdinalIgnoreCase);

    public static bool CanExecuteApplications(IEnumerable<string>? capabilities, string? purpose = null)
        => string.Equals(RunnerPurposes.Normalize(purpose), RunnerPurposes.Managed, StringComparison.Ordinal)
            && Normalize(capabilities, purpose).Contains(ApplicationHosting, StringComparer.OrdinalIgnoreCase);

    public static bool CanExecuteApplications(string? capabilitiesJson, string? purpose = null)
        => string.Equals(RunnerPurposes.Normalize(purpose), RunnerPurposes.Managed, StringComparison.Ordinal)
            && Includes(capabilitiesJson, ApplicationHosting, purpose);
}
