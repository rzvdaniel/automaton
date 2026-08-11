using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Automaton.AgentOrchestrator.Domain;

namespace Automaton.AgentOrchestrator.Codex;

public static class ExecutionIdentityClassifications
{
    public const string Dead = "dead";
    public const string MatchingLive = "matching-live";
    public const string Reused = "pid-reused";
    public const string Ambiguous = "ambiguous";
}

public sealed record ExecutionIdentityInspection(string Classification, string? Detail = null);
public sealed record FencedProcess(Process Process, DurableExecutionIdentity Identity);

internal static class ExecutionRootInspectionStates
{
    public const string Missing = "missing";
    public const string Live = "live";
    public const string Ambiguous = "ambiguous";
}

internal sealed record ExecutionRootInspection(string State, DateTimeOffset? StartedAtUtc = null);

internal interface IRestartProcessInspector
{
    ExecutionRootInspection InspectRoot(int processId);
    bool? IsProcessGroupAbsent(int processGroupId);
}

public interface IExecutionFence
{
    Task<FencedProcess> LaunchAsync(ProcessStartInfo startInfo, string launchNonce, CancellationToken cancellationToken);
    Task<ExecutionIdentityInspection> InspectAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken);
    Task<bool> TerminateAndVerifyAsync(DurableExecutionIdentity identity, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class PlatformExecutionFence : IExecutionFence
{
    private readonly ConcurrentDictionary<string, SafeFileHandle> windowsJobs = new(StringComparer.Ordinal);
    private readonly IRestartProcessInspector restartInspector;

    public PlatformExecutionFence() : this(new PlatformRestartProcessInspector()) { }
    internal PlatformExecutionFence(IRestartProcessInspector restartInspector) => this.restartInspector = restartInspector;

    public Task<FencedProcess> LaunchAsync(ProcessStartInfo startInfo, string launchNonce, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effective = OperatingSystem.IsLinux() ? WrapWithNewSession(startInfo) : startInfo;
        var process = Process.Start(effective) ?? throw new CodexProtocolException("Unable to launch Codex App Server.");
        var fencingKind = OperatingSystem.IsLinux() ? ExecutionFencingKinds.UnixProcessGroup : ExecutionFencingKinds.PortableProcessTree;
        int? processGroupId = OperatingSystem.IsLinux() ? process.Id : null;
        if (OperatingSystem.IsWindows() && TryAssignWindowsJob(process, launchNonce)) fencingKind = ExecutionFencingKinds.WindowsJob;
        var identity = new DurableExecutionIdentity(
            process.Id,
            process.StartTime.ToUniversalTime(),
            launchNonce,
            fencingKind,
            processGroupId,
            DateTimeOffset.UtcNow);
        return Task.FromResult(new FencedProcess(process, identity));
    }

    public Task<ExecutionIdentityInspection> InspectAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = restartInspector.InspectRoot(identity.ProcessId);
        return Task.FromResult(root.State switch
        {
            ExecutionRootInspectionStates.Live when root.StartedAtUtc == identity.ProcessStartedAtUtc
                => new ExecutionIdentityInspection(ExecutionIdentityClassifications.MatchingLive),
            ExecutionRootInspectionStates.Live => new ExecutionIdentityInspection(ExecutionIdentityClassifications.Reused),
            ExecutionRootInspectionStates.Missing => ClassifyMissingRoot(identity),
            _ => new ExecutionIdentityInspection(ExecutionIdentityClassifications.Ambiguous, "Process identity inspection failed.")
        });
    }

    private ExecutionIdentityInspection ClassifyMissingRoot(DurableExecutionIdentity identity)
    {
        if (identity.FencingKind == ExecutionFencingKinds.WindowsJob)
            return new(ExecutionIdentityClassifications.Dead);
        if (identity.FencingKind == ExecutionFencingKinds.UnixProcessGroup && identity.ProcessGroupId is { } groupId)
        {
            return restartInspector.IsProcessGroupAbsent(groupId) == true
                ? new(ExecutionIdentityClassifications.Dead)
                : new(ExecutionIdentityClassifications.Ambiguous, "The root process is absent but process-group ownership cannot be proven clear.");
        }
        return new(ExecutionIdentityClassifications.Ambiguous, "The root process is absent but descendant ownership cannot be proven clear.");
    }

    public async Task<bool> TerminateAndVerifyAsync(DurableExecutionIdentity identity, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var inspection = await InspectAsync(identity, cancellationToken);
        if (inspection.Classification == ExecutionIdentityClassifications.Dead)
        {
            if (OperatingSystem.IsWindows() && windowsJobs.TryRemove(identity.LaunchNonce, out var completedJob)) completedJob.Dispose();
            return true;
        }
        if (inspection.Classification != ExecutionIdentityClassifications.MatchingLive) return false;
        try
        {
            if (OperatingSystem.IsWindows() && windowsJobs.TryRemove(identity.LaunchNonce, out var job)) job.Dispose();
            else if (OperatingSystem.IsLinux() && identity.ProcessGroupId is { } groupId) NativeMethods.Kill(-groupId, NativeMethods.SigTerm);
            else
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                process.Kill(entireProcessTree: true);
            }

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                await process.WaitForExitAsync(timeoutCancellation.Token);
            }
            catch (ArgumentException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && OperatingSystem.IsLinux() && identity.ProcessGroupId is { } groupId)
            {
                NativeMethods.Kill(-groupId, NativeMethods.SigKill);
                using var killTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                killTimeout.CancelAfter(timeout);
                try
                {
                    using var process = Process.GetProcessById(identity.ProcessId);
                    await process.WaitForExitAsync(killTimeout.Token);
                }
                catch (ArgumentException) { }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
            }
            return (await InspectAsync(identity, cancellationToken)).Classification == ExecutionIdentityClassifications.Dead;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ProcessStartInfo WrapWithNewSession(ProcessStartInfo source)
    {
        var wrapped = new ProcessStartInfo("setsid")
        {
            WorkingDirectory = source.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = source.RedirectStandardInput,
            RedirectStandardOutput = source.RedirectStandardOutput,
            RedirectStandardError = source.RedirectStandardError,
            CreateNoWindow = source.CreateNoWindow
        };
        wrapped.Environment.Clear();
        foreach (var pair in source.Environment) wrapped.Environment[pair.Key] = pair.Value;
        wrapped.ArgumentList.Add(source.FileName);
        foreach (var argument in source.ArgumentList) wrapped.ArgumentList.Add(argument);
        return wrapped;
    }

    private bool TryAssignWindowsJob(Process process, string launchNonce)
    {
        var job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); return false; }
        var information = new NativeMethods.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation { LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose }
        };
        var length = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(information, pointer, false);
            if (!NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectExtendedLimitInformationClass, pointer, (uint)length)
                || !NativeMethods.AssignProcessToJobObject(job, process.Handle))
            {
                job.Dispose();
                return false;
            }
            windowsJobs[launchNonce] = job;
            return true;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static class NativeMethods
    {
        internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
        internal const int JobObjectExtendedLimitInformationClass = 9;
        internal const int SigTerm = 15;
        internal const int SigKill = 9;
        internal const int NoSuchProcess = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateJobObject(IntPtr securityAttributes, string? name);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, IntPtr information, uint informationLength);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int pid, int signal);

        [StructLayout(LayoutKind.Sequential)]
        internal struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
    }

    private sealed class PlatformRestartProcessInspector : IRestartProcessInspector
    {
        public ExecutionRootInspection InspectRoot(int processId)
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return new(ExecutionRootInspectionStates.Ambiguous);
            try
            {
                using var process = Process.GetProcessById(processId);
                return new(ExecutionRootInspectionStates.Live, new DateTimeOffset(process.StartTime.ToUniversalTime()));
            }
            catch (ArgumentException) { return new(ExecutionRootInspectionStates.Missing); }
            catch (InvalidOperationException) { return new(ExecutionRootInspectionStates.Missing); }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                return new(ExecutionRootInspectionStates.Ambiguous);
            }
        }

        public bool? IsProcessGroupAbsent(int processGroupId)
        {
            if (!OperatingSystem.IsLinux()) return null;
            if (NativeMethods.Kill(-processGroupId, 0) == 0) return false;
            return Marshal.GetLastPInvokeError() == NativeMethods.NoSuchProcess ? true : null;
        }
    }
}
