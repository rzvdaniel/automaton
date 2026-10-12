namespace Automaton.Node.Capabilities;

/// <summary>Caller-supplied, unverified facts. This type performs no host discovery.</summary>
public sealed record NodeCapabilityReport(
    string? OperatingSystem,
    string? Architecture,
    string? Engine,
    string? EngineVersion,
    string? CgroupMode,
    IReadOnlyList<NodeStorageCapability>? StorageIdentities);

public sealed record NodeStorageCapability(
    Guid VolumeId, string? FilesystemUuid, string? FilesystemType, int? ProjectId);
