using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Automaton.Runner.Console.Services;

internal static class LinuxManagedDirectoryPermissions
{
    private const int OpenReadOnly = 0;
    private const int OpenCloseOnExec = 0x80000;
    private const int X64DirectoryFlag = 0x10000;
    private const int X64OpenNoFollow = 0x20000;
    private const int Arm64DirectoryFlag = 0x4000;
    private const int Arm64OpenNoFollow = 0x8000;
    private const int NoEntryError = 2;
    private const int AlreadyExistsError = 17;
    private const uint DirectoryCreationMode = 0x1ED;
    private const uint WritableDirectoryMode = 0x1FF;

    internal static void PrepareWritableDirectory(string trustedRoot, string relativePath)
    {
        using var target = OpenManagedDirectory(trustedRoot, relativePath);
        SetWritableDirectoryPermissions(target);
    }

    internal static SafeFileHandle OpenManagedDirectory(string trustedRoot, string relativePath)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Handle-anchored application storage preparation requires Linux.");
        var openFlags = GetManagedDirectoryOpenFlags(RuntimeInformation.ProcessArchitecture);
        if (!Automaton.Core.Models.ManagedPathBoundary.IsSafeRelative(relativePath))
            throw new InvalidOperationException("Application data must remain inside its managed root.");

        var segments = GetPathSegments(relativePath).ToArray();
        if (segments.Length == 0)
            throw new InvalidOperationException("Application data must resolve below its managed root.");

        Directory.CreateDirectory(trustedRoot);
        var root = OpenDirectory(trustedRoot, openFlags);
        SafeFileHandle? current = null;
        try
        {
            foreach (var segment in segments)
            {
                var next = OpenOrCreateDirectory(current ?? root, segment, openFlags);
                current?.Dispose();
                current = next;
            }

            var result = current ?? throw new InvalidOperationException("The managed application directory was not opened.");
            current = null;
            return result;
        }
        catch
        {
            current?.Dispose();
            throw;
        }
        finally
        {
            root.Dispose();
        }
    }

    internal static void SetWritableDirectoryPermissions(SafeFileHandle directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (directory.IsInvalid || directory.IsClosed)
            throw new ArgumentException("An open directory handle is required.", nameof(directory));
        if (Fchmod(GetDescriptor(directory), WritableDirectoryMode) != 0)
            throw CreateFileSystemException("Could not prepare the managed application data directory.");
    }

    private static IEnumerable<string> GetPathSegments(string relativePath)
        => relativePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".");

    private static SafeFileHandle OpenDirectory(string path, int flags)
    {
        var descriptor = Open(path, flags);
        return CreateHandle(descriptor, "Could not open the trusted application data root.");
    }

    private static SafeFileHandle OpenOrCreateDirectory(SafeFileHandle parent, string name, int flags)
    {
        var parentDescriptor = GetDescriptor(parent);
        var descriptor = OpenAt(parentDescriptor, name, flags);
        if (descriptor < 0 && Marshal.GetLastPInvokeError() == NoEntryError)
        {
            if (MkdirAt(parentDescriptor, name, DirectoryCreationMode) != 0
                && Marshal.GetLastPInvokeError() != AlreadyExistsError)
                throw CreateFileSystemException("Could not create a managed application data directory.");

            descriptor = OpenAt(parentDescriptor, name, flags);
        }

        return CreateHandle(descriptor, "Application data path contains a symbolic link or non-directory component.");
    }

    internal static int GetManagedDirectoryOpenFlags(Architecture architecture)
    {
        var (directoryFlag, noFollowFlag) = architecture switch
        {
            Architecture.X64 => (X64DirectoryFlag, X64OpenNoFollow),
            Architecture.Arm64 => (Arm64DirectoryFlag, Arm64OpenNoFollow),
            _ => throw new PlatformNotSupportedException($"Linux managed storage permissions do not support {architecture}.")
        };

        return OpenReadOnly | directoryFlag | noFollowFlag | OpenCloseOnExec;
    }

    private static SafeFileHandle CreateHandle(int descriptor, string message)
    {
        if (descriptor < 0)
            throw CreateFileSystemException(message);
        return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }

    private static int GetDescriptor(SafeFileHandle handle)
        => checked((int)handle.DangerousGetHandle());

    private static IOException CreateFileSystemException(string message)
    {
        var error = Marshal.GetLastPInvokeError();
        return new IOException(message, new Win32Exception(error));
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MkdirAt(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static extern int Fchmod(int fileDescriptor, uint mode);
}
