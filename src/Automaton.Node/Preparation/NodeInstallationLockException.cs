namespace Automaton.Node.Preparation;

internal enum NodeInstallationLockFailure
{
    ConfigurationInvalid,
    UnsupportedPlatform,
    UnsafeDirectory,
    UnsupportedFileSystem,
    UnsafeLockFile,
    AlreadyHeld,
    OperatingSystemFailure
}

/// <summary>A fixed, non-sensitive startup diagnostic for lock acquisition failures.</summary>
internal sealed class NodeInstallationLockException(NodeInstallationLockFailure failure)
    : InvalidOperationException($"Node installation lock unavailable: {failure}.")
{
    public NodeInstallationLockFailure Failure { get; } = failure;
}
