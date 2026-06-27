using System.IO.Compression;
using Microsoft.Extensions.FileSystemGlobbing;
using Automaton.Cli.Models;
using Automaton.Core.Models;

namespace Automaton.Cli.Services;

/// <summary>
/// Creates a zip stream from a local directory, respecting an explicit deploy directory.
/// Files inside .git/, node_modules/, and obj/ are excluded.
/// automaton.json is excluded from the upload (it's local-only).
/// </summary>
public static class ZipService
{
    private const string IgnoreFileName = ".automatonignore";

    public static readonly string[] DefaultIgnorePatterns =
    {
        ".automaton/",
        ".github/",
        ".git/",
        ".venv/",
        "__pycache__/",
        "node_modules/",
        "obj/",
        "bin/",
        ".vs/",
        ".idea/",
        "AGENTS.md",
        "CLAUDE.md",
        "automaton.json",
        ".automatonignore"
    };

    public static readonly string[] SourceIgnorePatterns =
    {
        "frontend/dist/",
        "dist/",
        ".vite/",
        "coverage/"
    };

    /// <summary>
    /// Resolves which directory to zip from an explicit project-relative deploy directory.
    /// </summary>
    public static string ResolveDeployDirectory(string projectRoot, string? deployDir)
    {
        if (string.IsNullOrWhiteSpace(deployDir))
        {
            throw new InvalidOperationException(
                "frontend.deployDir is required in automaton.json. " +
                "Use 'frontend/dist' for build-based apps or '.' for an explicit no-build static app.");
        }

        var explicitPath = Path.GetFullPath(Path.Combine(projectRoot, deployDir));
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        if (!explicitPath.StartsWith(fullProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(explicitPath, fullProjectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"deployDir '{deployDir}' points outside the project root");
        }

        if (Directory.Exists(explicitPath)) return explicitPath;
        throw new DirectoryNotFoundException(
            $"deployDir '{deployDir}' configured in automaton.json does not exist: {explicitPath}");
    }

    /// <summary>
    /// Creates an in-memory zip of <paramref name="sourceDir"/> and returns the stream.
    /// The returned stream is positioned at 0 — caller is responsible for disposing.
    /// </summary>
    public static Stream CreateZip(string projectRoot, string sourceDir, DeployScopeConfig? scopeConfig = null)
    {
        return CreateZip(projectRoot, sourceDir, includeAutomatonConfig: false, additionalIgnorePatterns: null, scopeConfig: scopeConfig);
    }

    public static Stream CreateSourceZip(string projectRoot, DeployScopeConfig? scopeConfig = null)
    {
        return CreateZip(projectRoot, projectRoot, includeAutomatonConfig: true, additionalIgnorePatterns: SourceIgnorePatterns, scopeConfig: scopeConfig);
    }

    public static Stream CreateDeployPackageZip(
        string projectRoot,
        string appId,
        string frontendDeployDir,
        string? backendDeployDir,
        DeployScopeConfig? sourceScopeConfig,
        FrontendConfig? frontendConfig,
        BackendConfig? backendConfig)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(System.Text.Json.JsonSerializer.Serialize(new AppDeploymentPackageManifest
                {
                    AppId = appId,
                    HasBackend = !string.IsNullOrWhiteSpace(backendDeployDir)
                }));
            }

            AddDirectoryWithPrefix(
                archive,
                projectRoot,
                projectRoot,
                "source",
                includeAutomatonConfig: true,
                additionalIgnorePatterns: SourceIgnorePatterns,
                scopeConfig: sourceScopeConfig);

            AddDirectoryWithPrefix(
                archive,
                frontendDeployDir,
                frontendDeployDir,
                "runtime/frontend",
                includeAutomatonConfig: false,
                additionalIgnorePatterns: null,
                scopeConfig: frontendConfig == null
                    ? null
                    : new DeployScopeConfig
                    {
                        Include = frontendConfig.Include,
                        Exclude = frontendConfig.Exclude
                    });

            if (!string.IsNullOrWhiteSpace(backendDeployDir))
            {
                AddDirectoryWithPrefix(
                    archive,
                    backendDeployDir,
                    backendDeployDir,
                    "runtime/backend",
                    includeAutomatonConfig: false,
                    additionalIgnorePatterns: null,
                    scopeConfig: backendConfig == null
                        ? null
                        : new DeployScopeConfig
                        {
                            Include = backendConfig.Include,
                            Exclude = backendConfig.Exclude
                        });
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static Stream CreateZip(
        string projectRoot,
        string sourceDir,
        bool includeAutomatonConfig,
        IEnumerable<string>? additionalIgnorePatterns,
        DeployScopeConfig? scopeConfig)
    {
        var ignoreMatcher = CreateIgnoreMatcher(projectRoot, includeAutomatonConfig, additionalIgnorePatterns);
        var includeMatcher = CreateIncludeMatcher(scopeConfig?.Include);
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddDirectory(archive, sourceDir, sourceDir, ignoreMatcher, includeMatcher, scopeConfig?.Exclude);
        }

        ms.Position = 0;
        return ms;
    }

    private static void AddDirectory(
        ZipArchive archive,
        string rootDir,
        string currentDir,
        Matcher ignoreMatcher,
        Matcher? includeMatcher,
        IEnumerable<string>? additionalExcludes)
    {
        foreach (var file in Directory.GetFiles(currentDir))
        {
            var entryName = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            if (IsIgnored(ignoreMatcher, entryName) || IsExcludedByScope(entryName, includeMatcher, additionalExcludes)) continue;

            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        foreach (var dir in Directory.GetDirectories(currentDir))
        {
            var relativeDir = Path.GetRelativePath(rootDir, dir).Replace('\\', '/');
            if (IsIgnored(ignoreMatcher, relativeDir, isDirectory: true)) continue;

            AddDirectory(archive, rootDir, dir, ignoreMatcher, includeMatcher, additionalExcludes);
        }
    }

    private static void AddDirectoryWithPrefix(
        ZipArchive archive,
        string projectRoot,
        string sourceDir,
        string entryPrefix,
        bool includeAutomatonConfig,
        IEnumerable<string>? additionalIgnorePatterns,
        DeployScopeConfig? scopeConfig)
    {
        var ignoreMatcher = CreateIgnoreMatcher(projectRoot, includeAutomatonConfig, additionalIgnorePatterns);
        var includeMatcher = CreateIncludeMatcher(scopeConfig?.Include);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var entryName = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            if (IsIgnored(ignoreMatcher, entryName) || IsExcludedByScope(entryName, includeMatcher, scopeConfig?.Exclude))
            {
                continue;
            }

            archive.CreateEntryFromFile(file, $"{entryPrefix}/{entryName}", CompressionLevel.Optimal);
        }
    }

    private static Matcher CreateIgnoreMatcher(
        string projectRoot,
        bool includeAutomatonConfig,
        IEnumerable<string>? additionalIgnorePatterns)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

        foreach (var pattern in DefaultIgnorePatterns)
        {
            if (includeAutomatonConfig && pattern.Equals("automaton.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddIgnorePattern(matcher, pattern);
        }

        if (additionalIgnorePatterns != null)
        {
            foreach (var pattern in additionalIgnorePatterns)
            {
                AddIgnorePattern(matcher, pattern);
            }
        }

        foreach (var pattern in LoadCustomIgnorePatterns(projectRoot))
        {
            AddIgnorePattern(matcher, pattern);
        }

        return matcher;
    }

    private static Matcher? CreateIncludeMatcher(IEnumerable<string>? includePatterns)
    {
        if (includePatterns == null) return null;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        var hasPatterns = false;
        foreach (var pattern in includePatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            AddIncludePattern(matcher, pattern);
            hasPatterns = true;
        }

        return hasPatterns ? matcher : null;
    }

    private static IEnumerable<string> LoadCustomIgnorePatterns(string projectRoot)
    {
        var path = Path.Combine(projectRoot, IgnoreFileName);
        if (!File.Exists(path)) yield break;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("!", StringComparison.Ordinal))
                throw new InvalidOperationException($"{IgnoreFileName} does not support negation patterns yet: {line}");

            yield return line;
        }
    }

    private static void AddIgnorePattern(Matcher matcher, string pattern)
    {
        var normalized = pattern.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.TrimEnd('/');
            matcher.AddInclude(normalized);
            matcher.AddInclude($"{normalized}/**");
            matcher.AddInclude($"**/{normalized}");
            matcher.AddInclude($"**/{normalized}/**");
            return;
        }

        matcher.AddInclude(normalized);
    }

    private static void AddIncludePattern(Matcher matcher, string pattern)
    {
        var normalized = pattern.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.TrimEnd('/');
            matcher.AddInclude(normalized);
            matcher.AddInclude($"{normalized}/**");
            return;
        }

        matcher.AddInclude(normalized);
    }

    private static bool IsIgnored(Matcher matcher, string relativePath, bool isDirectory = false)
    {
        if (matcher.Match(relativePath).HasMatches)
            return true;

        return isDirectory && matcher.Match(relativePath + "/").HasMatches;
    }

    private static bool IsExcludedByScope(string relativePath, Matcher? includeMatcher, IEnumerable<string>? excludePatterns)
    {
        if (includeMatcher != null && !includeMatcher.Match(relativePath).HasMatches)
        {
            return true;
        }

        if (excludePatterns == null)
        {
            return false;
        }

        return excludePatterns.Any(pattern => MatchesPattern(relativePath, pattern));
    }

    private static bool MatchesPattern(string relativePath, string pattern)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        AddIncludePattern(matcher, pattern);
        return matcher.Match(relativePath).HasMatches;
    }
}
