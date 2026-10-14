namespace Automaton.Node.Diagnostics;

public enum NodeDoctorFreshness
{
    Current,
    Stale,
    Unknown,
    NotApplicable
}

public enum NodeDoctorCode
{
    NoObservationsSupplied,
    PlatformLinuxX64Reported,
    PlatformUnsupportedOs,
    PlatformUnsupportedArchitecture,
    PlatformUnknown,
    InstallationLockUnavailable,
    IdentityMissing,
    IdentityInvalid,
    IdentityStale,
    IdentityUnknown,
    RecoveryAuthorityMissing,
    RecoveryAuthorityStale,
    RecoveryAuthorityUnknown,
    RecoveryEpochMismatch,
    CredentialMissing,
    CredentialRevoked,
    CredentialStale,
    CredentialUnknown,
    ActivationRecoveryRequired,
    ActivationFenced,
    ActivationUnknown,
    JournalUnsettled,
    JournalUnknown,
    PackageUnverified,
    PackageInvalid,
    ServiceUnavailable,
    EngineUnsupported,
    EngineUnknown,
    CgroupUnsupported,
    CgroupUnknown,
    StorageUnsupported,
    StorageUnknown
}

public enum NodeDoctorSeverity
{
    Information,
    Warning,
    Blocking
}

public readonly record struct NodeDoctorFactV1(
    NodeDoctorCode Code,
    NodeDoctorFreshness Freshness,
    DateTimeOffset? ObservedAtUtc);

public sealed class NodeDoctorInputV1
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumFacts = 32;

    public NodeDoctorInputV1(int schemaVersion, IEnumerable<NodeDoctorFactV1> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        SchemaVersion = schemaVersion;
        var copy = new List<NodeDoctorFactV1>();
        foreach (var fact in facts)
        {
            if (copy.Count == MaximumFacts)
                throw new ArgumentException($"At most {MaximumFacts} doctor facts are allowed.", nameof(facts));
            copy.Add(fact);
        }
        Facts = Array.AsReadOnly(copy.ToArray());
    }

    public int SchemaVersion { get; }
    public IReadOnlyList<NodeDoctorFactV1> Facts { get; }
}

public sealed record NodeDoctorDiagnosticV1(
    NodeDoctorCode OutputCode,
    NodeDoctorSeverity Severity,
    NodeDoctorFreshness Freshness,
    DateTimeOffset? ObservedAtUtc,
    string Message);
