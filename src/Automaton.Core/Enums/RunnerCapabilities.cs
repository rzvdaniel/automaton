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
    private static readonly string[] PrivilegedCapabilities =
    [
        DockerManagement,
        HostExecution,
        HostedRunnerProvisioning,
        RouteManagement,
        CertificateManagement
    ];

    public const string ApplicationHosting = "ApplicationHosting";
    public const string WorkflowExecution = "WorkflowExecution";
    public const string DockerManagement = "DockerManagement";
    public const string HostExecution = "HostExecution";
    public const string HostedRunnerProvisioning = "HostedRunnerProvisioning";
    public const string RouteManagement = "RouteManagement";
    public const string CertificateManagement = "CertificateManagement";

    public static IReadOnlyList<string> DefaultsForPurpose(string? purpose)
        => string.Equals(
                RunnerPurposes.Normalize(purpose),
                RunnerPurposes.Infrastructure,
                StringComparison.Ordinal)
            ?
            [
                ApplicationHosting,
                WorkflowExecution,
                DockerManagement,
                HostExecution,
                HostedRunnerProvisioning,
                RouteManagement,
                CertificateManagement
            ]
            :
            [ApplicationHosting, WorkflowExecution];

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? capabilities, string? purpose)
    {
        var values = capabilities?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
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

    public static bool RequiresAdministrator(IEnumerable<string>? capabilities, string? purpose = null)
        => Normalize(capabilities, purpose)
            .Intersect(PrivilegedCapabilities, StringComparer.OrdinalIgnoreCase)
            .Any();

    public static bool RequiresAdministrator(string? capabilitiesJson, string? purpose = null)
        => Deserialize(capabilitiesJson, purpose)
            .Intersect(PrivilegedCapabilities, StringComparer.OrdinalIgnoreCase)
            .Any();

    public static bool CanExecuteApplications(IEnumerable<string>? capabilities, string? purpose = null)
        => string.Equals(RunnerPurposes.Normalize(purpose), RunnerPurposes.Managed, StringComparison.Ordinal)
            && Normalize(capabilities, purpose).Contains(ApplicationHosting, StringComparer.OrdinalIgnoreCase)
            && !RequiresAdministrator(capabilities, purpose);

    public static bool CanExecuteApplications(string? capabilitiesJson, string? purpose = null)
        => string.Equals(RunnerPurposes.Normalize(purpose), RunnerPurposes.Managed, StringComparison.Ordinal)
            && Includes(capabilitiesJson, ApplicationHosting, purpose)
            && !RequiresAdministrator(capabilitiesJson, purpose);
}
