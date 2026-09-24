using System.Runtime.InteropServices;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;

namespace Automaton.Runner.Console.Services;

internal static class InfrastructureRunnerRegistrationRequestFactory
{
    public static async Task<InfrastructureRunnerRegistrationRequest> CreateAsync(FileStorageService storageService)
    {
        var installationId = await storageService.GetOrCreateInstallationIdAsync();
        var purpose = RunnerPurposes.Infrastructure;
        var configuredRunnerName = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_NAME");
        return new InfrastructureRunnerRegistrationRequest
        {
            InstallationId = installationId,
            RunnerName = string.IsNullOrWhiteSpace(configuredRunnerName)
                ? $"infrastructure-{Environment.MachineName}"
                : configuredRunnerName,
            RunnerVersion = Environment.Version.ToString(),
            MachineName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            DockerVersion = Environment.GetEnvironmentVariable("AUTOMATON_DOCKER_VERSION"),
            PublicIp = Environment.GetEnvironmentVariable("AUTOMATON_PUBLIC_IP"),
            PrivateIp = Environment.GetEnvironmentVariable("AUTOMATON_PRIVATE_IP"),
            Provider = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PROVIDER"),
            Region = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_REGION"),
            CpuCores = Environment.ProcessorCount,
            MemoryMb = GetHostMemoryMb(),
            DiskMb = GetHostDiskMb(),
            Capabilities = RunnerCapabilities.ForInstallationProfile(
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                purpose)
        };
    }

    private static int? GetHostMemoryMb()
    {
        var memInfoPath = File.Exists("/host/proc/meminfo") ? "/host/proc/meminfo" : "/proc/meminfo";
        if (!File.Exists(memInfoPath)) return null;
        var line = File.ReadLines(memInfoPath)
            .FirstOrDefault(value => value.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var memoryKb)
            ? (int)Math.Clamp(memoryKb / 1024, 1, int.MaxValue)
            : null;
    }

    private static int? GetHostDiskMb()
    {
        try
        {
            var root = Directory.Exists("/host") ? "/host" : "/";
            return (int)Math.Clamp(new DriveInfo(root).TotalSize / (1024L * 1024L), 1, int.MaxValue);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
