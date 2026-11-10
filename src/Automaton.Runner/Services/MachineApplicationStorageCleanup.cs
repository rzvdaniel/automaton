using System.Text.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;

namespace Automaton.Runner.Console.Services;

/// <summary>Removes only verified app-owned Compose data volumes during whole-app deletion.</summary>
public sealed class MachineApplicationStorageCleanup
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<InfrastructureCommandResponse>> execute;

    private readonly string helperImage;
    private readonly bool? useWorkspaceVolume;
    private readonly string ownContainer = Environment.MachineName;

    public MachineApplicationStorageCleanup(InfrastructureCommandService docker, IOptions<MachineStaticServingOptions> options)
        : this((arguments, token) => docker.ExecuteTypedDockerAsync(arguments, TimeSpan.FromMinutes(2), token), options.Value.Image) { }

    internal MachineApplicationStorageCleanup(
        Func<IReadOnlyList<string>, CancellationToken, Task<InfrastructureCommandResponse>> execute,
        string helperImage = "nginx:alpine",
        bool? useWorkspaceVolume = null,
        string? ownContainer = null)
    {
        this.execute = execute;
        this.helperImage = helperImage;
        this.useWorkspaceVolume = useWorkspaceVolume;
        if (ownContainer is not null) this.ownContainer = ownContainer;
    }

    /// <summary>Empties a managed directory whose files the app's runtime user owns, using a short-lived helper container
    /// (no network, all capabilities dropped except DAC_OVERRIDE). The directory itself is left for the Runner to remove.
    /// A Runner in a container without a host workspace path mounts the workspace volume it already uses instead.</summary>
    public async Task EmptyDirectoryAsync(string path, CancellationToken token = default)
    {
        var (mount, target) = await WorkspaceMountAsync(path, token);
        var emptied = await execute(["docker", "run", "--rm", "--network", "none", "--user", "0",
            "--cap-drop", "ALL", "--cap-add", "DAC_OVERRIDE", "--security-opt", "no-new-privileges",
            "--mount", mount,
            helperImage, "find", target, "-mindepth", "1", "-delete"], token);
        if (!emptied.Success)
        {
            var detail = (emptied.Error ?? "").Trim();
            throw new InvalidOperationException("The Runner could not remove files the app's runtime user owns. App storage was preserved for retry."
                + (detail.Length == 0 ? "" : $" Docker said: {detail[..Math.Min(detail.Length, 200)]}"));
        }
    }

    /// <summary>The helper's mount and the path to empty inside it. Only the app's own managed directory is ever mounted or emptied.</summary>
    private async Task<(string Mount, string Target)> WorkspaceMountAsync(string path, CancellationToken token)
    {
        if (!UsesWorkspaceVolume()) return ($"type=bind,source={DockerContainerRuntime.MapHostPath(Path.GetFullPath(path))},target=/purge", "/purge");
        var full = path.Replace("\\", "/");
        if (!full.StartsWith('/') || full.Split('/').Any(part => part is "." or ".."))
            throw new InvalidOperationException("The app's storage path is not a managed absolute path. App storage was preserved for retry.");
        var inspected = await execute(["docker", "inspect", "--format", "{{json .Mounts}}", ownContainer], token);
        if (!inspected.Success) throw new InvalidOperationException("The Runner could not look up its own storage volume. App storage was preserved for retry.");
        try
        {
            var volume = JsonSerializer.Deserialize<List<ContainerMount>>(inspected.Output)?
                .Where(item => item.Type == "volume" && !string.IsNullOrEmpty(item.Name) && !string.IsNullOrEmpty(item.Destination)
                    && full.StartsWith(item.Destination.TrimEnd('/') + "/", StringComparison.Ordinal))
                .OrderByDescending(item => item.Destination.Length).FirstOrDefault();
            if (volume is not null) return ($"type=volume,source={volume.Name},target={volume.Destination}", full);
        }
        catch (JsonException) { }
        throw new InvalidOperationException("The app's storage is not on a volume the Runner can mount. App storage was preserved for retry.");
    }

    private sealed record ContainerMount(string Type, string Name, string Destination);

    /// <summary>A Runner in a container that was not given AUTOMATON_HOST_WORKSPACE_ROOT keeps its workspace on a Docker volume.</summary>
    private bool UsesWorkspaceVolume() => useWorkspaceVolume
        ?? (File.Exists("/.dockerenv") && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_HOST_WORKSPACE_ROOT")));

    public async Task DeleteComposeVolumesAsync(Guid appId, CancellationToken token = default)
    {
        if (appId == Guid.Empty) throw new ArgumentException("App identity is required.", nameof(appId));
        var listed = await execute(["docker", "volume", "ls", "--quiet",
            "--filter", $"label=com.automaton.app-id={appId:D}",
            "--filter", "label=com.automaton.resource=compose-app-volume",
            "--filter", "label=com.automaton.managed=true"], token);
        if (!listed.Success) throw new InvalidOperationException("Could not list app-owned Compose data volumes. Retry app deletion.");
        foreach (var volume in listed.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!volume.StartsWith("automaton-data-", StringComparison.Ordinal)
                || volume.Length != "automaton-data-".Length + 32
                || !Guid.TryParseExact(volume.AsSpan("automaton-data-".Length), "N", out _))
                throw new InvalidOperationException("Docker returned an invalid app data volume identity; it was preserved.");
            var inspected = await execute(["docker", "volume", "inspect", "--format", "{{json .Labels}}", volume], token);
            if (!inspected.Success || !IsOwnedVolume(inspected.Output, appId, volume))
                throw new InvalidOperationException("App data volume ownership verification failed; it was preserved.");
            var removed = await execute(["docker", "volume", "rm", volume], token);
            if (!removed.Success) throw new InvalidOperationException("Could not remove an app data volume. It may still be in use; retry after runtime cleanup.");
        }
    }

    private static bool IsOwnedVolume(string json, Guid appId, string volume)
    {
        try
        {
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (labels is null || labels.GetValueOrDefault("com.automaton.managed") != "true"
                || labels.GetValueOrDefault("com.automaton.resource") != "compose-app-volume"
                || labels.GetValueOrDefault("com.automaton.app-id") != appId.ToString("D")
                || !Guid.TryParse(labels.GetValueOrDefault("com.automaton.deployment-target-id"), out var targetId)
                || !labels.TryGetValue("com.automaton.compose-volume", out var name)) return false;
            return volume == HostContainerComposeDocumentBuilder.VolumeName(new(Guid.Empty, Guid.Empty, appId, targetId), name);
        }
        catch (JsonException) { return false; }
    }
}
