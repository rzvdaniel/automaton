using System.Runtime.InteropServices;
using Automaton.Node.Preparation;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeInstallationLockPathValidationTests
{
    [Test]
    public async Task NodeHostLock_RejectsSymlinkedDirectoryAndWritableDirectory()
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        var root = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        var realDirectory = Path.Combine(root, "real");
        Directory.CreateDirectory(realDirectory);
        NodeInstallationLockTestEnvironment.SetUnixMode(realDirectory, UserOnlyMode);
        var linkedDirectory = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(linkedDirectory, realDirectory);
        try
        {
            await AssertStartupFailureAsync(linkedDirectory, NodeInstallationLockFailure.UnsafeDirectory);
            NodeInstallationLockTestEnvironment.SetUnixMode(realDirectory, UserOnlyMode | UnixFileMode.GroupWrite);
            await AssertStartupFailureAsync(realDirectory, NodeInstallationLockFailure.UnsafeDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task NodeHostLock_RejectsSecureDirectoryUnderWritableAncestor()
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        var root = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        var unsafeParent = Path.Combine(root, "unsafe-parent");
        var secureFinalDirectory = Path.Combine(unsafeParent, "secure-final");
        Directory.CreateDirectory(secureFinalDirectory);
        NodeInstallationLockTestEnvironment.SetUnixMode(unsafeParent, UserOnlyMode | UnixFileMode.GroupWrite);
        NodeInstallationLockTestEnvironment.SetUnixMode(secureFinalDirectory, UserOnlyMode);

        try
        {
            await AssertStartupFailureAsync(secureFinalDirectory, NodeInstallationLockFailure.UnsafeDirectory);
        }
        finally
        {
            NodeInstallationLockTestEnvironment.SetUnixMode(unsafeParent, UserOnlyMode);
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task NodeHostLock_RejectsMissingRelativeAndUnsafeLockFiles()
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        await AssertStartupFailureAsync("relative-node-state", NodeInstallationLockFailure.ConfigurationInvalid);
        var root = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        try
        {
            await AssertStartupFailureAsync(Path.Combine(root, Guid.NewGuid().ToString("N")),
                NodeInstallationLockFailure.UnsafeDirectory);

            var lockPath = Path.Combine(root, "node-manager.lock");
            var target = Path.Combine(root, "target");
            await File.WriteAllTextAsync(target, string.Empty);
            File.CreateSymbolicLink(lockPath, target);
            await AssertStartupFailureAsync(root, expected: null);

            File.Delete(lockPath);
            File.Delete(target);
            Directory.CreateDirectory(lockPath);
            await AssertStartupFailureAsync(root, expected: null);

            Directory.Delete(lockPath);
            await File.WriteAllTextAsync(lockPath, string.Empty);
            NodeInstallationLockTestEnvironment.SetUnixMode(lockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.That(NodeInstallationLockTestEnvironment.Link(lockPath, Path.Combine(root, "second-link")), Is.Zero);
            await AssertStartupFailureAsync(root, NodeInstallationLockFailure.UnsafeLockFile);

            File.Delete(Path.Combine(root, "second-link"));
            Assert.That(NodeInstallationLockTestEnvironment.ChangeOwner(lockPath, 65534, 65534), Is.Zero);
            await AssertStartupFailureAsync(root, NodeInstallationLockFailure.UnsafeLockFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const UnixFileMode UserOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static async Task AssertStartupFailureAsync(string path, NodeInstallationLockFailure? expected)
    {
        using var host = NodePreparationHost.CreateBuilder(path).Build();
        var exception = Assert.ThrowsAsync<NodeInstallationLockException>(async () => await host.StartAsync());
        if (expected is not null)
            Assert.That(exception!.Failure, Is.EqualTo(expected));
    }
}

internal static class NodeInstallationLockTestEnvironment
{
    public static void RequireSupportedIntegrationEnvironment()
    {
        Assume.That(OperatingSystem.IsLinux(), Is.True, "The real-process lock suite requires Linux.");
        Assume.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(Architecture.X64));
        Assume.That(GetEffectiveUserId(), Is.Zero, "The lock fixture requires a root-owned test directory.");
    }

    public static string CreatePrivateTemporaryDirectory()
    {
        var configuredTestRoot = Environment.GetEnvironmentVariable("AUTOMATON_NODE_TEST_ROOT");
        var testRoot = string.IsNullOrWhiteSpace(configuredTestRoot) ? "/run" : configuredTestRoot;
        var directory = Path.Combine(testRoot, $"automaton-node-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        SetUnixMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }

    public static void SetUnixMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();
        File.SetUnixFileMode(path, mode);
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    public static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("libc", EntryPoint = "chown", SetLastError = true)]
    public static extern int ChangeOwner([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint owner, uint group);
}
