namespace Automaton.Node.Capabilities;

public static class NodeCapabilityValidator
{
    // Protocol input bounds, not deployment resources or entitlement defaults.
    public const int MaximumTextLength = 128;
    public const int MaximumStorageIdentities = 64;

    public static NodeCapabilityValidation Validate(NodeCapabilityReport? report)
    {
        if (report is null)
            return new(null, Array.AsReadOnly(new[] { NodeCapabilityDiagnostic.ReportMissing }));

        var errors = new HashSet<NodeCapabilityDiagnostic>();
        ValidateChoice(report.OperatingSystem, "linux", NodeCapabilityDiagnostic.OperatingSystemMissing,
            NodeCapabilityDiagnostic.OperatingSystemUnsupported, errors);
        ValidateChoice(report.Architecture, "x64", NodeCapabilityDiagnostic.ArchitectureMissing,
            NodeCapabilityDiagnostic.ArchitectureUnsupported, errors);
        ValidateChoice(report.Engine, "docker", NodeCapabilityDiagnostic.EngineMissing,
            NodeCapabilityDiagnostic.EngineUnsupported, errors);
        ValidateText(report.EngineVersion, NodeCapabilityDiagnostic.EngineVersionMissing, errors);
        ValidateChoice(report.CgroupMode, "v2", NodeCapabilityDiagnostic.CgroupModeMissing,
            NodeCapabilityDiagnostic.CgroupModeUnsupported, errors);
        ValidateStorage(report.StorageIdentities, errors);

        var snapshot = errors.Count == 0
            ? report with { StorageIdentities = Array.AsReadOnly(report.StorageIdentities!.ToArray()) }
            : null;
        return new(snapshot, Array.AsReadOnly(errors.Order().ToArray()));
    }

    private static bool ValidateText(string? value, NodeCapabilityDiagnostic missing,
        HashSet<NodeCapabilityDiagnostic> errors)
    {
        if (value is null)
        {
            errors.Add(missing);
            return false;
        }
        if (value.Length > MaximumTextLength)
        {
            errors.Add(NodeCapabilityDiagnostic.InvalidText);
            return false;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(missing);
            return false;
        }
        if (value.Any(char.IsControl))
        {
            errors.Add(NodeCapabilityDiagnostic.InvalidText);
            return false;
        }
        return true;
    }

    private static void ValidateChoice(string? value, string supported,
        NodeCapabilityDiagnostic missing, NodeCapabilityDiagnostic unsupported,
        HashSet<NodeCapabilityDiagnostic> errors)
    {
        if (ValidateText(value, missing, errors)
            && !string.Equals(value, supported, StringComparison.OrdinalIgnoreCase))
            errors.Add(unsupported);
    }

    private static void ValidateStorage(IReadOnlyList<NodeStorageCapability>? storage,
        HashSet<NodeCapabilityDiagnostic> errors)
    {
        if (storage is null || storage.Count == 0)
        {
            errors.Add(NodeCapabilityDiagnostic.StorageMissing);
            return;
        }
        if (storage.Count > MaximumStorageIdentities)
        {
            errors.Add(NodeCapabilityDiagnostic.TooManyStorageIdentities);
            return;
        }
        var volumes = new HashSet<Guid>();
        foreach (var item in storage)
            ValidateStorageIdentity(item, volumes, errors);
    }

    private static void ValidateStorageIdentity(NodeStorageCapability? item, HashSet<Guid> volumes,
        HashSet<NodeCapabilityDiagnostic> errors)
    {
        if (item is null)
        {
            errors.Add(NodeCapabilityDiagnostic.InvalidStorageIdentity);
            return;
        }
        ValidateChoice(item.FilesystemType, "xfs", NodeCapabilityDiagnostic.FilesystemMissing,
            NodeCapabilityDiagnostic.FilesystemUnsupported, errors);
        var uuidValid = ValidateText(item.FilesystemUuid, NodeCapabilityDiagnostic.InvalidStorageIdentity, errors)
            && Guid.TryParseExact(item.FilesystemUuid, "D", out var uuid) && uuid != Guid.Empty;
        if (item.VolumeId == Guid.Empty || item.ProjectId is null or <= 0 || !uuidValid)
            errors.Add(NodeCapabilityDiagnostic.InvalidStorageIdentity);
        if (!volumes.Add(item.VolumeId))
            errors.Add(NodeCapabilityDiagnostic.DuplicateVolumeIdentity);
    }
}
