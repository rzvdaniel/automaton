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
        ValidateSupportedDockerDriver(driver);

        var rootFilesystem = ParseMount(await ReadMountAsync("/", executeHostCommandAsync, cancellationToken));
        var dockerFilesystem = ParseMount(await ReadMountAsync(dockerRoot, executeHostCommandAsync, cancellationToken));
        ValidateBoundedLayout(rootFilesystem, dockerFilesystem);
    }

    public static void ValidateSupportedDockerDriver(string driver)
    {
        if (!string.Equals(driver.Trim(), "overlay2", StringComparison.Ordinal))
            throw new InvalidOperationException("Hosted Runner provisioning requires Docker's overlay2 storage driver.");
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

    public static void ValidateBoundedLayout(FilesystemMount rootFilesystem, FilesystemMount dockerFilesystem)
    {
        if (!IsXfsProjectFilesystem(dockerFilesystem))
            throw new InvalidOperationException("Docker image/cache data must reside on an XFS filesystem with project quotas enabled.");
        if (dockerFilesystem.Target == "/" || SameFilesystem(rootFilesystem, dockerFilesystem))
            throw new InvalidOperationException("Docker image/cache data must be on a separate filesystem from the operating-system root.");
    }

    private static bool IsXfsProjectFilesystem(FilesystemMount mount)
    {
        var options = mount.Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Equals(mount.FilesystemType, "xfs", StringComparison.OrdinalIgnoreCase)
            && options.Any(option => option is "prjquota" or "pquota");
    }

    private static bool SameFilesystem(FilesystemMount left, FilesystemMount right)
        => string.Equals(left.Uuid, right.Uuid, StringComparison.OrdinalIgnoreCase);

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

    internal sealed record FilesystemMount(string Target, string Source, string FilesystemType, string Uuid, string Options);
}
