using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Automaton.Node.Preparation;

/// <summary>Linux x64, root-owned host lock. Never removes or replaces the lock inode.</summary>
internal sealed class NodeInstallationLock(string? hostStateDirectory) : INodeInstallationLock, IDisposable
{
    private const int ReadWrite = 2;
    private const int Create = 0x40;
    private const int CloseOnExec = 0x80000;
    private const int NoFollow = 0x20000;
    private const int Directory = 0x10000;
    private const int Exclusive = 2;
    private const int NonBlocking = 4;
    private const int LockUnavailableErrno = 11;
    private const uint FileTypeMask = 0xF000;
    private const uint DirectoryType = 0x4000;
    private const uint RegularFileType = 0x8000;
    private const uint ForbiddenDirectoryModes = 0x12; // group/other write
    private const uint RequiredLockFileMode = 0x180; // 0600
    private const uint RequiredStateDirectoryMode = 0x1C0; // 0700
    private const uint RootUserId = 0;
    // Linux x64 struct stat offsets; other ABIs are rejected before these buffers are read.
    private const int LinkCountOffset = 16;
    private const int ModeOffset = 24;
    private const int UserIdOffset = 28;
    private const int FileSystemMagicOffset = 0; // fstatfs.f_type is the first native long.
    private const long ExtFileSystem = 0xEF53;
    private const long XfsFileSystem = 0x58465342;
    private const long BtrfsFileSystem = 0x9123683E;
    private const long TmpfsFileSystem = 0x01021994;
    private static readonly HashSet<long> SupportedFileSystems =
        [ExtFileSystem, XfsFileSystem, BtrfsFileSystem, TmpfsFileSystem];
    private const string LockFileName = "node-manager.lock";

    private readonly object sync = new();
    private SafeFileHandle? lockHandle;
    private SafeFileHandle? stateDirectoryHandle;

    internal NodeProtectedStateDirectory? StateDirectory
    {
        get
        {
            lock (sync)
                return lockHandle is not null && stateDirectoryHandle is not null
                    ? new NodeProtectedStateDirectory(stateDirectoryHandle)
                    : null;
        }
    }

    public void Acquire(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRuntimeAndConfiguration();
        var fullPath = GetFullPath(hostStateDirectory!);

        lock (sync)
        {
            if (lockHandle is not null)
                throw Failure(NodeInstallationLockFailure.OperatingSystemFailure);

            SafeFileHandle? candidateHandle = null;
            SafeFileHandle? directoryHandle = null;
            try
            {
                directoryHandle = OpenValidatedDirectory(fullPath);
                candidateHandle = OpenValidatedLockFile(directoryHandle);
                cancellationToken.ThrowIfCancellationRequested();
                AcquireKernelLock(candidateHandle);
                lockHandle = candidateHandle;
                candidateHandle = null;
                stateDirectoryHandle = directoryHandle;
                directoryHandle = null;
            }
            finally
            {
                candidateHandle?.Dispose();
                directoryHandle?.Dispose();
            }
        }
    }

    private void ValidateRuntimeAndConfiguration()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64
            || GetEffectiveUserId() != RootUserId)
            throw Failure(NodeInstallationLockFailure.UnsupportedPlatform);
        if (string.IsNullOrWhiteSpace(hostStateDirectory) || !Path.IsPathFullyQualified(hostStateDirectory))
            throw Failure(NodeInstallationLockFailure.ConfigurationInvalid);
    }

    private static string GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Failure(NodeInstallationLockFailure.ConfigurationInvalid);
        }
    }

    private static SafeFileHandle OpenValidatedDirectory(string path)
    {
        SafeFileHandle? current = OpenRootDirectory();
        try
        {
            ValidateDirectory(current);
            foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var child = OpenDirectoryAt(current, component);
                try
                {
                    ValidateDirectory(child);
                }
                catch
                {
                    child.Dispose();
                    throw;
                }
                current.Dispose();
                current = child;
            }

            ValidateProtectedStateDirectory(current);
            EnsureSupportedFileSystem(current);
            var finalDirectory = current;
            current = null;
            return finalDirectory;
        }
        finally
        {
            current?.Dispose();
        }
    }

    private static void ValidateDirectory(SafeFileHandle handle)
    {
        var metadata = ReadMetadata(handle);
        if ((metadata.Mode & FileTypeMask) != DirectoryType || metadata.UserId != RootUserId
            || (metadata.Mode & ForbiddenDirectoryModes) != 0)
            throw Failure(NodeInstallationLockFailure.UnsafeDirectory);
    }

    private static void ValidateProtectedStateDirectory(SafeFileHandle handle)
    {
        var metadata = ReadMetadata(handle);
        if ((metadata.Mode & FileTypeMask) != DirectoryType || metadata.UserId != RootUserId ||
            (metadata.Mode & 0xFFF) != RequiredStateDirectoryMode)
            throw Failure(NodeInstallationLockFailure.UnsafeDirectory);
    }

    private static SafeFileHandle OpenValidatedLockFile(SafeFileHandle directory)
    {
        var descriptor = OpenAt(directory, LockFileName,
            ReadWrite | Create | CloseOnExec | NoFollow, RequiredLockFileMode);
        if (descriptor < 0)
            throw Failure(NodeInstallationLockFailure.OperatingSystemFailure);
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            var metadata = ReadMetadata(handle);
            if ((metadata.Mode & FileTypeMask) != RegularFileType || metadata.UserId != RootUserId
                || metadata.LinkCount != 1 || (metadata.Mode & 0x1FF) != RequiredLockFileMode)
                throw Failure(NodeInstallationLockFailure.UnsafeLockFile);
            EnsureSupportedFileSystem(handle);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void AcquireKernelLock(SafeFileHandle handle)
    {
        if (Flock(handle, Exclusive | NonBlocking) == 0)
            return;
        throw Failure(Marshal.GetLastPInvokeError() == LockUnavailableErrno
            ? NodeInstallationLockFailure.AlreadyHeld
            : NodeInstallationLockFailure.OperatingSystemFailure);
    }

    public void Release()
    {
        SafeFileHandle? handle;
        SafeFileHandle? directory;
        lock (sync)
        {
            handle = lockHandle;
            lockHandle = null;
            directory = stateDirectoryHandle;
            stateDirectoryHandle = null;
        }
        handle?.Dispose();
        directory?.Dispose();
    }

    public void Dispose() => Release();

    private static NodeInstallationLockException Failure(NodeInstallationLockFailure failure) => new(failure);

    private static SafeFileHandle OpenRootDirectory()
    {
        var descriptor = Open("/", ReadOnly | Directory | CloseOnExec | NoFollow, 0);
        if (descriptor < 0)
            throw Failure(NodeInstallationLockFailure.UnsafeDirectory);
        return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }

    private static SafeFileHandle OpenDirectoryAt(SafeFileHandle parent, string component)
    {
        var descriptor = OpenAt(parent, component, ReadOnly | Directory | CloseOnExec | NoFollow, 0);
        if (descriptor < 0)
            throw Failure(NodeInstallationLockFailure.UnsafeDirectory);
        return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }

    private static NodeFileMetadata ReadMetadata(SafeFileHandle handle)
    {
        var buffer = new byte[256];
        if (FStat(handle, buffer) != 0)
            throw Failure(NodeInstallationLockFailure.OperatingSystemFailure);
        return new NodeFileMetadata(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(ModeOffset, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(UserIdOffset, 4)),
            BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(LinkCountOffset, 8)));
    }

    private static void EnsureSupportedFileSystem(SafeFileHandle handle)
    {
        var buffer = new byte[256];
        if (FStatFileSystem(handle, buffer) != 0)
            throw Failure(NodeInstallationLockFailure.OperatingSystemFailure);
        var fileSystem = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(FileSystemMagicOffset, 8));
        if (!SupportedFileSystems.Contains(fileSystem))
            throw Failure(NodeInstallationLockFailure.UnsupportedFileSystem);
    }

    private const int ReadOnly = 0;

    [DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mode);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(SafeFileHandle file, [Out] byte[] buffer);

    [DllImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static extern int FStatFileSystem(SafeFileHandle file, [Out] byte[] buffer);

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(SafeFileHandle file, int operation);

    private readonly record struct NodeFileMetadata(uint Mode, uint UserId, ulong LinkCount);
}
