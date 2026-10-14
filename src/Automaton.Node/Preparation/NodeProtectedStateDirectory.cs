using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Automaton.Node.Preparation;

/// <summary>File operations relative to the directory handle held by the installation lock.</summary>
internal sealed class NodeProtectedStateDirectory(
    SafeFileHandle directory,
    Action<string, NodeProtectedStateWriteStage>? beforeWriteOperation = null)
{
    private const int CloseOnExec = 0x80000;
    private const int NoFollow = 0x20000;
    private const int Create = 0x40;
    private const int Exclusive = 0x80;
    private const int WriteOnly = 1;
    private const uint RootUserId = 0;
    private const uint RegularFileType = 0x8000;
    private const uint RequiredFileMode = 0x180;
    private const uint FileTypeMask = 0xF000;
    private const int LinkCountOffset = 16;
    private const int ModeOffset = 24;
    private const int UserIdOffset = 28;
    private const int SizeOffset = 48;
    private const int MissingFileErrno = 2;

    public byte[]? Read(string name, int maximumBytes)
    {
        ValidateName(name);
        if (maximumBytes is <= 0 or > 20 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        var descriptor = OpenAt(directory, name, CloseOnExec | NoFollow, 0);
        if (descriptor < 0)
        {
            if (Marshal.GetLastPInvokeError() == MissingFileErrno)
                return null;
            throw new NodeProtectedStateException();
        }

        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        var metadata = ReadMetadata(handle);
        if (!IsProtectedRegularFile(metadata) || metadata.Size < 0 || metadata.Size > maximumBytes)
            throw new NodeProtectedStateException();

        using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
        var content = new byte[checked((int)metadata.Size)];
        var offset = 0;
        while (offset < content.Length)
        {
            var read = stream.Read(content, offset, content.Length - offset);
            if (read == 0)
                throw new NodeProtectedStateException();
            offset += read;
        }
        if (stream.ReadByte() != -1 || !IsProtectedRegularFile(ReadMetadata(stream.SafeFileHandle)))
            throw new NodeProtectedStateException();
        return content;
    }

    public void AtomicReplace(string name, ReadOnlySpan<byte> content)
    {
        ValidateName(name);
        if (content.Length is 0 or > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(content));

        var temporaryName = $".node-state-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}.tmp";
        var descriptor = OpenAt(directory, temporaryName, WriteOnly | Create | Exclusive | CloseOnExec | NoFollow,
            RequiredFileMode);
        if (descriptor < 0)
            throw new NodeProtectedStateException();

        using (var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true))
        {
            if (!IsProtectedRegularFile(ReadMetadata(handle)))
                throw new NodeProtectedStateException();
            using var stream = new FileStream(handle, FileAccess.Write, 4096, isAsync: false);
            stream.Write(content);
            beforeWriteOperation?.Invoke(name, NodeProtectedStateWriteStage.FileFsync);
            stream.Flush(flushToDisk: true);
            if (!IsProtectedRegularFile(ReadMetadata(stream.SafeFileHandle)))
                throw new NodeProtectedStateException();
        }

        beforeWriteOperation?.Invoke(name, NodeProtectedStateWriteStage.Rename);
        if (RenameAt(directory, temporaryName, directory, name) != 0)
            throw new NodeProtectedStateException();
        beforeWriteOperation?.Invoke(name, NodeProtectedStateWriteStage.DirectoryFsync);
        if (Fsync(directory) != 0)
            throw new NodeProtectedStateException();
    }

    private static bool IsProtectedRegularFile(NodeFileMetadata metadata) =>
        (metadata.Mode & FileTypeMask) == RegularFileType && metadata.UserId == RootUserId &&
        metadata.LinkCount == 1 && (metadata.Mode & 0xFFF) == RequiredFileMode;

    private static NodeFileMetadata ReadMetadata(SafeFileHandle handle)
    {
        var buffer = new byte[256];
        if (FStat(handle, buffer) != 0)
            throw new NodeProtectedStateException();
        return new NodeFileMetadata(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(ModeOffset, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(UserIdOffset, 4)),
            BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(LinkCountOffset, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(SizeOffset, 8)));
    }

    private static void ValidateName(string name)
    {
        if (name is not ("node-identity.json" or "activation-checkpoint.json" or "node-client-cert.der" or
            "node-client-key.pem" or "node-api-key"))
            throw new ArgumentException("Unsupported Node state file name.", nameof(name));
    }

    private readonly record struct NodeFileMetadata(uint Mode, uint UserId, ulong LinkCount, long Size);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mode);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(SafeFileHandle file, [Out] byte[] buffer);

    [DllImport("libc", EntryPoint = "renameat", SetLastError = true)]
    private static extern int RenameAt(SafeFileHandle oldDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        SafeFileHandle newDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(SafeFileHandle file);
}

internal enum NodeProtectedStateWriteStage
{
    FileFsync,
    Rename,
    DirectoryFsync
}

internal sealed class NodeProtectedStateException : IOException
{
    public NodeProtectedStateException() : base("Protected Node state is unavailable or invalid.") { }
}
