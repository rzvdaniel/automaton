using System.Reflection;

namespace Automaton.Cli.Services;

public class VersionService
{
    private const string PackageId = "automaton.cli";
    private const string StoreDirectoryName = ".store";

    public virtual CliVersionInfo GetVersionInfo()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var buildVersion = informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var installedPackageVersion = TryGetInstalledPackageVersion(assembly.Location);

        return new CliVersionInfo(
            installedPackageVersion ?? buildVersion,
            buildVersion,
            installedPackageVersion);
    }

    public void WriteVersion(TextWriter writer)
    {
        var versionInfo = GetVersionInfo();
        writer.WriteLine($"Version: {versionInfo.DisplayVersion}");

        if (!string.Equals(versionInfo.BuildVersion, versionInfo.DisplayVersion, StringComparison.Ordinal))
        {
            writer.WriteLine($"Build version: {versionInfo.BuildVersion}");
        }
    }

    public static string? TryGetInstalledPackageVersion(string? assemblyLocation)
    {
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            return null;
        }

        var segments = assemblyLocation
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < segments.Length - 2; index++)
        {
            if (!string.Equals(segments[index], StoreDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(segments[index + 1], PackageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return segments[index + 2];
        }

        return null;
    }
}

public sealed record CliVersionInfo(string DisplayVersion, string BuildVersion, string? InstalledPackageVersion);