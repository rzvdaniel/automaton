using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Automaton.Node;
using Automaton.Node.Preparation;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;

namespace Automaton.Node.Tests;

internal sealed class NodeProcessFixture : IDisposable
{
    private Process? first;
    private Process? contender;
    private Process? restarted;
    private NodeLockFileIdentity originalIdentity;

    public async Task StartFirstAsync(string stateDirectory)
    {
        first = StartNodeProcess(stateDirectory);
        var firstOutput = await ReadUntilAsync(first, "RecoveryRequired; admission disabled.");
        NUnit.Framework.Assert.That(firstOutput, Does.Contain("installation lock held"));
        originalIdentity = ReadFileIdentity(Path.Combine(stateDirectory, "node-manager.lock"));
    }

    public async Task<string> VerifyContenderRejectedAsync(string stateDirectory)
    {
        contender = StartNodeProcess(stateDirectory);
        await contender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        NUnit.Framework.Assert.That(contender.ExitCode, Is.EqualTo(2));
        NUnit.Framework.Assert.That(ReadFileIdentity(Path.Combine(stateDirectory, "node-manager.lock")),
            Is.EqualTo(originalIdentity));
        return await contender.StandardError.ReadToEndAsync();
    }

    public async Task KillFirstAndRestartAsync(string stateDirectory, string lockPath)
    {
        first!.Kill(entireProcessTree: true);
        await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        restarted = StartNodeProcess(stateDirectory);
        var output = await ReadUntilAsync(restarted, "RecoveryRequired; admission disabled.");
        NUnit.Framework.Assert.That(output, Does.Contain("admission disabled"));
        NUnit.Framework.Assert.That(ReadFileIdentity(lockPath), Is.EqualTo(originalIdentity));
    }

    public void Dispose()
    {
        StopProcess(restarted);
        StopProcess(contender);
        StopProcess(first);
    }

    private static Process StartNodeProcess(string stateDirectory)
    {
        var assembly = typeof(NodePreparationHost).Assembly.Location;
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add("--runtimeconfig");
        process.StartInfo.ArgumentList.Add(Path.ChangeExtension(assembly, ".runtimeconfig.json"));
        process.StartInfo.ArgumentList.Add("--depsfile");
        process.StartInfo.ArgumentList.Add(Path.ChangeExtension(assembly, ".deps.json"));
        process.StartInfo.ArgumentList.Add(assembly);
        process.StartInfo.Environment[Program.HostStateDirectoryEnvironmentVariable] = stateDirectory;
        if (!process.Start())
            throw new InvalidOperationException("Node process fixture did not start.");
        return process;
    }

    private static async Task<string> ReadUntilAsync(Process process, string expectedText)
    {
        var output = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
        {
            output.Add(line);
            var text = string.Join(Environment.NewLine, output);
            if (text.Contains(expectedText, StringComparison.Ordinal))
                return text;
            if (process.HasExited)
                break;
        }
        throw new NUnit.Framework.AssertionException(
            $"Node process exited before the startup state appeared. Output: {string.Join(Environment.NewLine, output)}");
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
            return;
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(15_000);
        }
        process.Dispose();
    }

    private static NodeLockFileIdentity ReadFileIdentity(string path)
    {
        var descriptor = Open(path, ReadOnly | CloseOnExec);
        if (descriptor < 0)
            throw new IOException("Unable to open the Node lock fixture.");
        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        var metadata = new byte[256];
        if (FStat(handle, metadata) != 0)
            throw new IOException("Unable to inspect the Node lock fixture.");
        return new NodeLockFileIdentity(
            BinaryPrimitives.ReadUInt64LittleEndian(metadata.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(metadata.AsSpan(8, 8)));
    }

    private const int ReadOnly = 0;
    private const int CloseOnExec = 0x80000;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(SafeFileHandle file, [Out] byte[] buffer);

    private readonly record struct NodeLockFileIdentity(ulong Device, ulong Inode);
}
