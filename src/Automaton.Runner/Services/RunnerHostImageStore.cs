using System.Text.Json;

namespace Automaton.Runner.Console.Services;

internal static class RunnerHostImageStore
{
    public static async Task VerifyBoundedLayoutAsync(
        string dockerRoot,
        Func<IReadOnlyList<string>, CancellationToken, Task<string>> executeHostCommandAsync,
        CancellationToken cancellationToken)
    {
        var driver = await executeHostCommandAsync(["docker", "info", "--format", "{{.Driver}}"], cancellationToken);
        var status = await executeHostCommandAsync(["docker", "info", "--format", "{{json .DriverStatus}}"], cancellationToken);
        var imageStore = Parse(driver, status);
        var imageStoreRoot = dockerRoot;
        if (imageStore.UsesContainerd)
        {
            var dockerProcesses = await executeHostCommandAsync(["sh", "-c", "ps -C dockerd -o args= 2>/dev/null || true"], cancellationToken);
            var containerdProcesses = await executeHostCommandAsync(["sh", "-c", "ps -C containerd -o args= 2>/dev/null || true"], cancellationToken);
            imageStoreRoot = await ResolveContainerdRootAsync(
                dockerRoot, dockerProcesses, containerdProcesses, executeHostCommandAsync, cancellationToken);
        }

        var dockerMount = ParseMount(await ReadMountAsync(dockerRoot, executeHostCommandAsync, cancellationToken));
        var imageStoreMount = string.Equals(dockerRoot, imageStoreRoot, StringComparison.Ordinal)
            ? dockerMount
            : ParseMount(await ReadMountAsync(imageStoreRoot, executeHostCommandAsync, cancellationToken));
        ValidateBoundedLayout(dockerMount, imageStoreMount);
    }

    public static ImageStore Parse(string driver, string driverStatusJson)
    {
        using var status = JsonDocument.Parse(driverStatusJson);
        if (status.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Docker did not identify its image storage backend.");

        foreach (var item in status.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2
                || item[0].ValueKind != JsonValueKind.String
                || !string.Equals(item[0].GetString(), "driver-type", StringComparison.OrdinalIgnoreCase))
                continue;

            var type = item[1].ValueKind == JsonValueKind.String ? item[1].GetString() : null;
            if (type?.StartsWith("io.containerd.snapshotter.", StringComparison.Ordinal) == true)
                return new(true);
            throw new InvalidOperationException($"Docker reported an unsupported image storage backend '{type ?? "unknown"}'.");
        }

        return driver.Trim() switch
        {
            "overlay2" or "overlayfs" or "aufs" or "btrfs" or "zfs" or "devicemapper" or "vfs" or "fuse-overlayfs" => new(false),
            _ => throw new InvalidOperationException("Docker's image storage backend could not be identified; host storage is not admitted until it can be verified.")
        };
    }

    public static async Task<string> ResolveContainerdRootAsync(
        string dockerRoot,
        string dockerProcessOutput,
        string containerdProcessOutput,
        Func<IReadOnlyList<string>, CancellationToken, Task<string>> executeHostCommandAsync,
        CancellationToken cancellationToken)
    {
        var dockerProcesses = SplitProcesses(dockerProcessOutput);
        var dockerArguments = RequireSingleProcess(dockerProcesses, "Docker daemon");
        var dockerSocket = ReadOption(dockerArguments, "--containerd") ?? "/run/containerd/containerd.sock";
        var containerdProcesses = SplitProcesses(containerdProcessOutput);
        var activeProcesses = containerdProcesses
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(arguments => string.Equals(
                ReadOption(arguments, "--address", "-a") ?? "/run/containerd/containerd.sock",
                dockerSocket,
                StringComparison.Ordinal))
            .ToArray();
        var arguments = RequireSingleProcessArguments(activeProcesses, "active containerd");
        var rootOverride = ReadOption(arguments, "--root", "-r");
        if (rootOverride is not null)
            return RequireAbsoluteRoot(rootOverride);

        var configPath = ReadOption(arguments, "--config", "-c") ?? "/etc/containerd/config.toml";
        var config = await executeHostCommandAsync(["containerd", "--config", configPath, "config", "dump"], cancellationToken);
        return ParseContainerdRoot(config);
    }

    private static string[] SplitProcesses(string output)
        => output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] RequireSingleProcess(IReadOnlyList<string> processes, string processName)
    {
        if (processes.Count != 1)
            throw new InvalidOperationException($"The active {processName} process could not be identified unambiguously.");
        return processes[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] RequireSingleProcessArguments(IReadOnlyList<string[]> processes, string processName)
    {
        if (processes.Count != 1)
            throw new InvalidOperationException($"The active {processName} process could not be identified unambiguously.");
        return processes[0];
    }

    public static FilesystemMount ParseMount(string output)
    {
        using var document = JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("filesystems", out var filesystems)
            || filesystems.ValueKind != JsonValueKind.Array
            || filesystems.GetArrayLength() != 1)
            throw new InvalidOperationException("The host did not return one filesystem for a storage root.");

        var filesystem = filesystems[0];
        var mount = new FilesystemMount(
            ReadString(filesystem, "target"),
            ReadString(filesystem, "source"),
            ReadString(filesystem, "fstype"),
            ReadString(filesystem, "uuid"),
            ReadString(filesystem, "options"));
        if (string.IsNullOrWhiteSpace(mount.Target) || string.IsNullOrWhiteSpace(mount.Source)
            || string.IsNullOrWhiteSpace(mount.FilesystemType) || string.IsNullOrWhiteSpace(mount.Uuid))
            throw new InvalidOperationException("The host did not return complete filesystem identity for a storage root.");
        return mount;
    }

    public static void ValidateBoundedLayout(FilesystemMount docker, FilesystemMount imageStore)
    {
        if (!IsSupportedMount(docker) || !IsSupportedMount(imageStore))
            throw new InvalidOperationException("Docker and image-store data must reside on a mounted XFS filesystem with project quotas enabled.");
        if (string.Equals(docker.Target, "/", StringComparison.Ordinal)
            || !string.Equals(docker.Target, imageStore.Target, StringComparison.Ordinal)
            || !string.Equals(docker.Uuid, imageStore.Uuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Docker data-root and image/cache storage must share one bounded, non-root XFS filesystem. Configure both roots explicitly; Automaton will not move data.");
    }

    private static bool IsSupportedMount(FilesystemMount mount)
    {
        var options = mount.Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Equals(mount.FilesystemType, "xfs", StringComparison.OrdinalIgnoreCase)
            && options.Any(option => option is "prjquota" or "pquota");
    }

    private static string ParseContainerdRoot(string config)
    {
        foreach (var line in config.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
                break;
            var separator = trimmed.IndexOf('=');
            if (separator < 0 || !string.Equals(trimmed[..separator].Trim(), "root", StringComparison.Ordinal))
                continue;
            var value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
            return RequireAbsoluteRoot(value);
        }

        throw new InvalidOperationException("The active containerd configuration did not identify its image-store root.");
    }

    private static string? ReadOption(IReadOnlyList<string> arguments, params string[] optionNames)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            foreach (var optionName in optionNames)
            {
                if (argument.StartsWith(optionName + "=", StringComparison.Ordinal))
                    return argument[(optionName.Length + 1)..];
                if (argument == optionName)
                    return index + 1 < arguments.Count ? arguments[index + 1] : null;
            }
        }
        return null;
    }

    private static string RequireAbsoluteRoot(string root)
    {
        var normalized = root.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains('\0'))
            throw new InvalidOperationException("Containerd did not return an absolute image-store root.");
        return normalized;
    }

    private static Task<string> ReadMountAsync(
        string path,
        Func<IReadOnlyList<string>, CancellationToken, Task<string>> executeHostCommandAsync,
        CancellationToken cancellationToken)
        => executeHostCommandAsync(
            ["findmnt", "--json", "--target", path, "--output", "TARGET,SOURCE,FSTYPE,UUID,OPTIONS"],
            cancellationToken);

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    internal sealed record ImageStore(bool UsesContainerd);
    internal sealed record FilesystemMount(string Target, string Source, string FilesystemType, string Uuid, string Options);
}
