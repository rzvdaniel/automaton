using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class HostContainerComposeDocumentBuilder
{
    public static object Build(
        ApplicationComposeSpec compose,
        IReadOnlyDictionary<string, string> resolvedImages,
        HostContainerScope scope,
        Guid candidateId,
        string address,
        HostContainerResourceLimits limits,
        int sharedMemoryMb,
        int logMaxSizeMb,
        int logMaxFiles,
        IReadOnlyList<HostContainerGrantedMount>? grantedMounts = null)
    {
        grantedMounts ??= [];
        var services = compose.Services.ToDictionary(service => service.Name,
            service => CreateService(service, compose, resolvedImages, scope, candidateId, address,
                limits, sharedMemoryMb, logMaxSizeMb, logMaxFiles, grantedMounts), StringComparer.Ordinal);
        var volumes = compose.Volumes.ToDictionary(name => name,
            name => (object)new { name = VolumeName(scope, name), external = true }, StringComparer.Ordinal);
        foreach (var grant in grantedMounts)
            // The app's own volumes must never be replaced by (or alias) a granted one.
            if (!volumes.TryAdd(GrantedVolumeKey(grant), new { name = GrantedVolumeName(grant), external = true }))
                throw new InvalidOperationException("A declared volume uses the name reserved for a granted data volume.");
        return new
        {
            services,
            volumes,
            networks = new { @default = new { labels = NetworkLabels(scope, candidateId) } }
        };
    }

    public static string ProjectName(HostContainerScope scope, Guid candidateId)
        => "automaton-" + StableHash($"{scope.AppId:N}:{scope.DeploymentTargetId:N}:{candidateId:N}")[..24];

    public static string VolumeName(HostContainerScope scope, string volume)
        => "automaton-data-" + StableHash($"{scope.AppId:N}:{scope.DeploymentTargetId:N}:{volume}")[..32];

    public static string GrantedVolumeName(HostContainerGrantedMount grant)
        => VolumeName(new HostContainerScope(Guid.Empty, Guid.Empty, grant.SourceAppId, grant.SourceDeploymentTargetId), grant.Volume);

    private static string GrantedVolumeKey(HostContainerGrantedMount grant)
        => "granted_" + grant.SourceAppId.ToString("N")[..12] + "_" + grant.Volume;

    public static string CandidateLabel(Guid candidateId)
        => $"com.automaton.compose-candidate={candidateId:N}";

    private static Dictionary<string, object> CreateService(
        ApplicationComposeService service,
        ApplicationComposeSpec compose,
        IReadOnlyDictionary<string, string> resolvedImages,
        HostContainerScope scope,
        Guid candidateId,
        string address,
        HostContainerResourceLimits limits,
        int sharedMemoryMb,
        int logMaxSizeMb,
        int logMaxFiles,
        IReadOnlyList<HostContainerGrantedMount> grantedMounts)
    {
        var image = resolvedImages[service.Name];
        var config = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["image"] = image,
            ["read_only"] = true,
            ["tmpfs"] = new[] { "/tmp:rw,nosuid,nodev,size=32m,mode=1777" },
            ["cap_drop"] = new[] { "ALL" },
            ["security_opt"] = new[] { "no-new-privileges:true" },
            ["shm_size"] = $"{sharedMemoryMb}m",
            ["cpus"] = limits.CpuLimitCores,
            ["mem_limit"] = $"{limits.MemoryLimitMb}M",
            ["pids_limit"] = limits.PidsLimit,
            ["logging"] = new { driver = "local", options = new Dictionary<string, string> { ["max-size"] = $"{logMaxSizeMb}m", ["max-file"] = logMaxFiles.ToString(CultureInfo.InvariantCulture) } },
            ["labels"] = ContainerLabels(scope, candidateId, image)
        };
        var isPublic = string.Equals(service.Name, compose.PublicService, StringComparison.Ordinal);
        AddOptionalConfiguration(config, service, scope, isPublic ? grantedMounts : []);
        if (isPublic)
            config["ports"] = new[] { new { target = service.Port!.Value, host_ip = address, protocol = "tcp" } };
        return config;
    }

    private static void AddOptionalConfiguration(
        IDictionary<string, object> config,
        ApplicationComposeService service,
        HostContainerScope scope,
        IReadOnlyList<HostContainerGrantedMount> grantedMounts)
    {
        if (service.Command.Count > 0) config["command"] = service.Command;
        if (service.Entrypoint.Count > 0) config["entrypoint"] = service.Entrypoint;
        if (service.WorkingDirectory is not null) config["working_dir"] = service.WorkingDirectory;
        if (service.Environment.Count > 0) config["environment"] = service.Environment;
        if (service.DependsOn.Count > 0) config["depends_on"] = service.DependsOn;
        if (service.Mounts.Count == 0 && grantedMounts.Count == 0) return;
        config["volumes"] = service.Mounts.Select(mount =>
                $"{mount.Name}:{mount.Target}{(mount.ReadOnly ? ":ro" : string.Empty)}")
            .Concat(grantedMounts.Select(grant =>
                $"{GrantedVolumeKey(grant)}:{grant.Target}{(grant.ReadOnly ? ":ro" : string.Empty)}")).ToArray();
    }

    private static Dictionary<string, string> ContainerLabels(
        HostContainerScope scope,
        Guid candidateId,
        string resolvedImage)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["com.automaton.managed"] = "true",
            ["com.automaton.runtime-kind"] = "generic",
            ["com.automaton.app-id"] = scope.AppId.ToString(),
            ["com.automaton.deployment-target-id"] = scope.DeploymentTargetId.ToString(),
            ["com.automaton.compose-candidate"] = candidateId.ToString("N"),
            ["com.automaton.resolved-image"] = resolvedImage
        };
        labels["com.automaton.machine-runner-id"] = scope.MachineRunnerId.ToString();
        return labels;
    }

    private static Dictionary<string, string> NetworkLabels(HostContainerScope scope, Guid candidateId)
        => new(StringComparer.Ordinal)
        {
            ["com.automaton.managed"] = "true",
            ["com.automaton.app-id"] = scope.AppId.ToString(),
            ["com.automaton.deployment-target-id"] = scope.DeploymentTargetId.ToString(),
            ["com.automaton.compose-candidate"] = candidateId.ToString("N")
        };

    private static string StableHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
