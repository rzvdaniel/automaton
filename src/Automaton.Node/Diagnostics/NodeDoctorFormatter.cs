namespace Automaton.Node.Diagnostics;

/// <summary>Formats caller-supplied normalized facts without probing or granting host authority.</summary>
public static class NodeDoctorFormatter
{
    private static readonly IReadOnlyDictionary<NodeDoctorCode, DiagnosticDefinition> Definitions =
        new Dictionary<NodeDoctorCode, DiagnosticDefinition>
        {
            [NodeDoctorCode.PlatformLinuxX64Reported] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Information, "Reported platform profile: Linux x64."),
            [NodeDoctorCode.PlatformUnsupportedOs] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported operating system is outside the supported Node profile."),
            [NodeDoctorCode.PlatformUnsupportedArchitecture] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported architecture is outside the supported Node profile."),
            [NodeDoctorCode.PlatformUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The platform profile is unknown."),
            [NodeDoctorCode.InstallationLockUnavailable] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The installation lock is unavailable."),
            [NodeDoctorCode.IdentityMissing] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The protected Node identity is missing."),
            [NodeDoctorCode.IdentityInvalid] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The protected Node identity is invalid."),
            [NodeDoctorCode.IdentityStale] = Define(NodeDoctorFreshness.Stale, NodeDoctorSeverity.Blocking, "The protected Node identity is stale."),
            [NodeDoctorCode.IdentityUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The protected Node identity state is unknown."),
            [NodeDoctorCode.RecoveryAuthorityMissing] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "Recovery authority evidence is missing."),
            [NodeDoctorCode.RecoveryAuthorityStale] = Define(NodeDoctorFreshness.Stale, NodeDoctorSeverity.Blocking, "Recovery authority evidence is stale."),
            [NodeDoctorCode.RecoveryAuthorityUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "Recovery authority state is unknown."),
            [NodeDoctorCode.RecoveryEpochMismatch] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The recovery epoch does not match the current authority."),
            [NodeDoctorCode.CredentialMissing] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The Node credential is missing."),
            [NodeDoctorCode.CredentialRevoked] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node credential is revoked."),
            [NodeDoctorCode.CredentialStale] = Define(NodeDoctorFreshness.Stale, NodeDoctorSeverity.Blocking, "The Node credential status is stale."),
            [NodeDoctorCode.CredentialUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The Node credential status is unknown."),
            [NodeDoctorCode.ActivationRecoveryRequired] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "Node activation requires recovery."),
            [NodeDoctorCode.ActivationFenced] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node activation is fenced."),
            [NodeDoctorCode.ActivationUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The Node activation state is unknown."),
            [NodeDoctorCode.JournalUnsettled] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "A durable Node operation remains unsettled."),
            [NodeDoctorCode.JournalUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The durable Node operation state is unknown."),
            [NodeDoctorCode.PackageUnverified] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "Node package provenance is unverified."),
            [NodeDoctorCode.PackageInvalid] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node package failed verification."),
            [NodeDoctorCode.ServiceUnavailable] = Define(NodeDoctorFreshness.Current, NodeDoctorSeverity.Blocking, "The Node service is unavailable."),
            [NodeDoctorCode.EngineUnsupported] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported container engine is unsupported."),
            [NodeDoctorCode.EngineUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The container engine state is unknown."),
            [NodeDoctorCode.CgroupUnsupported] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported cgroup profile is unsupported."),
            [NodeDoctorCode.CgroupUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The cgroup profile is unknown."),
            [NodeDoctorCode.StorageUnsupported] = Define(NodeDoctorFreshness.NotApplicable, NodeDoctorSeverity.Blocking, "The reported storage profile is unsupported."),
            [NodeDoctorCode.StorageUnknown] = Define(NodeDoctorFreshness.Unknown, NodeDoctorSeverity.Blocking, "The storage profile is unknown.")
        };

    public static IReadOnlyList<NodeDoctorDiagnosticV1> Format(NodeDoctorInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);
        if (input.Facts.Count == 0)
        {
            return Array.AsReadOnly(new[]
            {
                new NodeDoctorDiagnosticV1(NodeDoctorCode.NoObservationsSupplied, NodeDoctorSeverity.Information,
                    NodeDoctorFreshness.Unknown, null, "No diagnostic observations were supplied.")
            });
        }

        var diagnostics = input.Facts.Select(fact => ToDiagnostic(fact, Definitions[fact.Code]))
            .OrderBy(diagnostic => diagnostic.OutputCode.ToString(), StringComparer.Ordinal)
            .ToArray();
        return Array.AsReadOnly(diagnostics);
    }

    private static void ValidateInput(NodeDoctorInputV1 input)
    {
        if (input.SchemaVersion != NodeDoctorInputV1.CurrentSchemaVersion)
            throw new ArgumentException("Unsupported doctor input schema version.", nameof(input));

        var seenCodes = new HashSet<NodeDoctorCode>();
        foreach (var fact in input.Facts)
        {
            if (!Definitions.TryGetValue(fact.Code, out var definition))
                throw new ArgumentException("Doctor input contains an undefined diagnostic code.", nameof(input));
            if (fact.Freshness != definition.Freshness)
                throw new ArgumentException("Doctor fact freshness does not match its diagnostic code.", nameof(input));
            if (fact.ObservedAtUtc is { Offset: var offset } && offset != TimeSpan.Zero)
                throw new ArgumentException("Doctor observation times must use UTC.", nameof(input));
            if (!seenCodes.Add(fact.Code))
                throw new ArgumentException("Doctor input contains a duplicate diagnostic code.", nameof(input));
        }
    }

    private static NodeDoctorDiagnosticV1 ToDiagnostic(NodeDoctorFactV1 fact, DiagnosticDefinition definition) =>
        new(fact.Code, definition.Severity, fact.Freshness, fact.ObservedAtUtc, definition.Message);

    private static DiagnosticDefinition Define(NodeDoctorFreshness freshness, NodeDoctorSeverity severity, string message) =>
        new(freshness, severity, message);

    private sealed record DiagnosticDefinition(
        NodeDoctorFreshness Freshness,
        NodeDoctorSeverity Severity,
        string Message);
}
