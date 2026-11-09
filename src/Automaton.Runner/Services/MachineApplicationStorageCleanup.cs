using System.Text.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;

namespace Automaton.Runner.Console.Services;

/// <summary>Removes only verified app-owned Compose data volumes during whole-app deletion.</summary>
public sealed class MachineApplicationStorageCleanup
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<InfrastructureCommandResponse>> execute;

    private readonly string helperImage;

    public MachineApplicationStorageCleanup(InfrastructureCommandService docker, IOptions<MachineStaticServingOptions> options)
        : this((arguments, token) => docker.ExecuteTypedDockerAsync(arguments, TimeSpan.FromMinutes(2), token), options.Value.Image) { }

    internal MachineApplicationStorageCleanup(
        Func<IReadOnlyList<string>, CancellationToken, Task<InfrastructureCommandResponse>> execute,
        string helperImage = "nginx:alpine")
    {
        this.execute = execute;
        this.helperImage = helperImage;
    }

    /// <summary>Empties a managed directory whose files the app's runtime user owns, using a short-lived helper container
    /// (no network, all capabilities dropped except DAC_OVERRIDE). The directory itself is left for the Runner to remove.</summary>
    public async Task EmptyDirectoryAsync(string path, CancellationToken token = default)
    {
        var emptied = await execute(["docker", "run", "--rm", "--network", "none", "--user", "0",
            "--cap-drop", "ALL", "--cap-add", "DAC_OVERRIDE", "--security-opt", "no-new-privileges",
            "--mount", $"type=bind,source={DockerContainerRuntime.MapHostPath(path)},target=/purge",
            helperImage, "find", "/purge", "-mindepth", "1", "-delete"], token);
        if (!emptied.Success)
        {
            var detail = (emptied.Error ?? "").Trim();
            throw new InvalidOperationException("The Runner could not remove files the app's runtime user owns. App storage was preserved for retry."
                + (detail.Length == 0 ? "" : $" Docker said: {detail[..Math.Min(detail.Length, 200)]}"));
        }
    }

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
