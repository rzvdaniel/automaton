namespace Automaton.Node.Capabilities;

public enum NodeCapabilityDiagnostic
{
    ReportMissing,
    InvalidText,
    OperatingSystemMissing,
    OperatingSystemUnsupported,
    ArchitectureMissing,
    ArchitectureUnsupported,
    EngineMissing,
    EngineUnsupported,
    EngineVersionMissing,
    CgroupModeMissing,
    CgroupModeUnsupported,
    StorageMissing,
    TooManyStorageIdentities,
    InvalidStorageIdentity,
    DuplicateVolumeIdentity,
    FilesystemMissing,
    FilesystemUnsupported
}

/// <summary>Validation of reported syntax/support only, never verified host eligibility.</summary>
public sealed record NodeCapabilityValidation(
    NodeCapabilityReport? Report,
    IReadOnlyList<NodeCapabilityDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
    public bool CanAdmitWorkloads { get; }
    public IReadOnlyList<string> EligibleWorkloadKinds { get; } = Array.Empty<string>();
}
