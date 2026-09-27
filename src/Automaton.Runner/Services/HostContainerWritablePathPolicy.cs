namespace Automaton.Runner.Console.Services;

internal static class HostContainerWritablePathPolicy
{
    private const int TemporaryDirectoryLimitMb = 32;

    public static void AddTo(
        ICollection<string> arguments,
        int sharedMemoryMb,
        int dockerLogMaxSizeMb,
        int dockerLogMaxFiles)
    {
        arguments.Add("--read-only");
        arguments.Add("--tmpfs");
        arguments.Add($"/tmp:rw,nosuid,nodev,size={TemporaryDirectoryLimitMb}m,mode=1777");
        arguments.Add("--shm-size");
        arguments.Add($"{sharedMemoryMb}m");
        arguments.Add("--log-driver");
        arguments.Add("local");
        arguments.Add("--log-opt");
        arguments.Add($"max-size={dockerLogMaxSizeMb}m");
        arguments.Add("--log-opt");
        arguments.Add($"max-file={dockerLogMaxFiles}");
    }
}
