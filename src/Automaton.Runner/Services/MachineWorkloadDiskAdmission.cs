using System.Globalization;

namespace Automaton.Runner.Console.Services;

/// <summary>Refuses new machine work when its managed workspace or Docker-backed filesystem is low on space.</summary>
public sealed class MachineWorkloadDiskAdmission(Func<string, long>? availableFreeBytes = null)
{
    private readonly Func<string, long> readAvailableFreeBytes = availableFreeBytes ?? ReadAvailableFreeBytes;

    public void EnsureAvailable(RunnerWorkspaceService workspace, int minimumFreeDiskMb)
    {
        if (minimumFreeDiskMb is < 256 or > 1_048_576)
            throw new InvalidOperationException("Machine work is paused because a valid minimum free-disk threshold was not provided.");

        var minimumBytes = checked((long)minimumFreeDiskMb * 1024 * 1024);
        var roots = new[]
        {
            workspace.WorkspaceRoot,
            Path.GetPathRoot(AppContext.BaseDirectory)
                ?? throw new InvalidOperationException("Machine work is paused because the Runner storage filesystem cannot be identified.")
        };
        foreach (var root in roots)
        {
            try
            {
                var availableBytes = readAvailableFreeBytes(root);
                if (availableBytes < minimumBytes)
                    throw Refusal(minimumFreeDiskMb, availableBytes);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new InvalidOperationException(
                    "Machine work is paused because free space on its workspace or Runner storage could not be measured.", exception);
            }
        }
    }

    private static long ReadAvailableFreeBytes(string path)
        => new DriveInfo(path).AvailableFreeSpace;

    private static InvalidOperationException Refusal(int minimumFreeDiskMb, long availableBytes)
    {
        var availableMb = Math.Max(0, availableBytes / (1024 * 1024));
        return new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
            "Machine work was refused because only {0} MB is free; the configured minimum is {1} MB.",
            availableMb, minimumFreeDiskMb));
    }
}
