using System.IO.Compression;
using Microsoft.Extensions.FileSystemGlobbing;
using Automaton.Cli.Models;
using Automaton.Core.Models;

namespace Automaton.Cli.Services;

/// <summary>
/// Creates a zip stream from a local directory, respecting an explicit deploy directory.
/// Automaton and version-control state are always excluded. Project-specific
/// exclusions are read from .automatonignore.
/// </summary>
public static class ZipService
{
    private const string IgnoreFileName = ".automatonignore";
    private sealed record IgnoreRule(Matcher Matcher, bool IsNegation, string Pattern);

    public static readonly string[] MandatoryIgnorePatterns =
    {
        ".automaton/",
        ".git/",
        "automaton.json",
        ".automatonignore"
    };

    public static readonly string[] ScaffoldIgnorePatterns =
    {
        "# Version-control and editor files",
        ".github/",
        ".vs/",
        ".idea/",
        "",
        "# Automaton and AI tooling",
        "AGENTS.md",
        "CLAUDE.md",
        "",
        "# Node and frontend build output",
        "node_modules/",
        "dist/",
        ".vite/",
        ".angular/",
        ".next/",
        "out/",
        "coverage/",
        "",
        "# Python",
        ".venv/",
        "__pycache__/",
        "*.pyc",
        "",
        "# .NET",
        "bin/",
        "obj/",
        "",
        "# Local environment, logs, and databases",
        ".env",
        ".env.*",
        "!.env.example",
        "*.log",
        "*.db",
        "*.sqlite",
        "*.sqlite3"
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

        return ResolveProjectDirectory(projectRoot, deployDir, "deployDir");
    }

    public static string ResolveProjectDirectory(string projectRoot, string? configuredDirectory, string settingName)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            throw new InvalidOperationException($"{settingName} is required in automaton.json");
        }

        var explicitPath = Path.GetFullPath(Path.Combine(projectRoot, configuredDirectory));
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        if (!explicitPath.StartsWith(fullProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(explicitPath, fullProjectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{settingName} '{configuredDirectory}' points outside the project root");
        }

        if (Directory.Exists(explicitPath)) return explicitPath;
        throw new DirectoryNotFoundException(
            $"{settingName} '{configuredDirectory}' configured in automaton.json does not exist: {explicitPath}");
    }

    /// <summary>
    /// Creates an in-memory zip of <paramref name="sourceDir"/> and returns the stream.
    /// The returned stream is positioned at 0 — caller is responsible for disposing.
    /// </summary>
    public static Stream CreateZip(string projectRoot, string sourceDir, DeployScopeConfig? scopeConfig = null)
    {
        return CreateZip(projectRoot, sourceDir, includeAutomatonConfig: false, scopeConfig: scopeConfig);
    }

    public static Stream CreateSourceZip(string projectRoot, DeployScopeConfig? scopeConfig = null)
    {
        return CreateZip(projectRoot, projectRoot, includeAutomatonConfig: true, scopeConfig: scopeConfig);
    }

    public static Stream CreateDeployPackageZip(
        string projectRoot,
        string appId,
        string? frontendDeployDir,
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
                additionalMandatoryIgnorePatterns: GetBackendRuntimeSourceExclusions(
                    projectRoot,
                    backendDeployDir,
                    backendConfig),
                scopeConfig: sourceScopeConfig);

            if (!string.IsNullOrWhiteSpace(frontendDeployDir))
            {
                AddDirectoryWithPrefix(
                    archive,
                    frontendDeployDir,
                    frontendDeployDir,
                    "runtime/frontend",
                    includeAutomatonConfig: false,
                    additionalMandatoryIgnorePatterns: null,
                    scopeConfig: frontendConfig == null
                        ? null
                        : new DeployScopeConfig
                        {
                            Include = frontendConfig.Include,
                            Exclude = frontendConfig.Exclude
                        });
            }

            if (!string.IsNullOrWhiteSpace(backendDeployDir))
            {
                AddDirectoryWithPrefix(
                    archive,
                    backendDeployDir,
                    backendDeployDir,
                    "runtime/backend",
                    includeAutomatonConfig: false,
                    additionalMandatoryIgnorePatterns: null,
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
        DeployScopeConfig? scopeConfig)
    {
        var ignoreMatchers = CreateIgnoreMatchers(projectRoot, includeAutomatonConfig);
        var includeMatcher = CreateIncludeMatcher(scopeConfig?.Include);
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddDirectory(archive, sourceDir, sourceDir, ignoreMatchers, includeMatcher, scopeConfig?.Exclude);
        }

        ms.Position = 0;
        return ms;
    }

    private static void AddDirectory(
        ZipArchive archive,
        string rootDir,
        string currentDir,
        (Matcher Mandatory, IReadOnlyList<IgnoreRule> Project) ignoreMatchers,
        Matcher? includeMatcher,
        IEnumerable<string>? additionalExcludes)
    {
        foreach (var file in Directory.GetFiles(currentDir))
        {
            var entryName = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            if (IsIgnored(ignoreMatchers, entryName) || IsExcludedByScope(entryName, includeMatcher, additionalExcludes)) continue;

            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        foreach (var dir in Directory.GetDirectories(currentDir))
        {
            var relativeDir = Path.GetRelativePath(rootDir, dir).Replace('\\', '/');
            if (ignoreMatchers.Mandatory.Match(relativeDir).HasMatches
                || ignoreMatchers.Mandatory.Match(relativeDir + "/").HasMatches)
            {
                continue;
            }

            var projectResult = EvaluateProjectRules(ignoreMatchers.Project, relativeDir, isDirectory: true);
            if (projectResult.IsIgnored
                && !CanLaterNegationReopenDirectory(ignoreMatchers.Project, projectResult.LastMatchIndex, relativeDir))
            {
                continue;
            }

            AddDirectory(archive, rootDir, dir, ignoreMatchers, includeMatcher, additionalExcludes);
        }
    }

    private static void AddDirectoryWithPrefix(
        ZipArchive archive,
        string projectRoot,
        string sourceDir,
        string entryPrefix,
        bool includeAutomatonConfig,
        IEnumerable<string>? additionalMandatoryIgnorePatterns,
        DeployScopeConfig? scopeConfig)
    {
        var ignoreMatchers = CreateIgnoreMatchers(
            projectRoot,
            includeAutomatonConfig,
            additionalMandatoryIgnorePatterns);
        var includeMatcher = CreateIncludeMatcher(scopeConfig?.Include);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var entryName = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            if (IsIgnored(ignoreMatchers, entryName) || IsExcludedByScope(entryName, includeMatcher, scopeConfig?.Exclude))
            {
                continue;
            }

            archive.CreateEntryFromFile(file, $"{entryPrefix}/{entryName}", CompressionLevel.Optimal);
        }
    }

    private static (Matcher Mandatory, IReadOnlyList<IgnoreRule> Project) CreateIgnoreMatchers(
        string projectRoot,
        bool includeAutomatonConfig,
        IEnumerable<string>? additionalMandatoryIgnorePatterns = null)
    {
        var mandatoryMatcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        var projectRules = new List<IgnoreRule>();

        foreach (var pattern in MandatoryIgnorePatterns)
        {
            if (includeAutomatonConfig && pattern.Equals("automaton.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddIgnorePattern(mandatoryMatcher, pattern);
        }

        if (additionalMandatoryIgnorePatterns != null)
        {
            foreach (var pattern in additionalMandatoryIgnorePatterns)
            {
                AddIgnorePattern(mandatoryMatcher, pattern);
            }
        }

        foreach (var pattern in LoadCustomIgnorePatterns(projectRoot))
        {
            projectRules.Add(CreateIgnoreRule(pattern));
        }

        return (mandatoryMatcher, projectRules);
    }

    private static IEnumerable<string> GetBackendRuntimeSourceExclusions(
        string projectRoot,
        string? backendDeployDirectory,
        BackendConfig? backendConfig)
    {
        if (string.IsNullOrWhiteSpace(backendDeployDirectory)
            || string.IsNullOrWhiteSpace(backendConfig?.SourceDir))
        {
            yield break;
        }

        var fullProjectRoot = Path.GetFullPath(projectRoot);
        var fullSourceDirectory = Path.GetFullPath(Path.Combine(fullProjectRoot, backendConfig.SourceDir));
        var fullDeployDirectory = Path.GetFullPath(backendDeployDirectory);
        if (string.Equals(fullSourceDirectory, fullDeployDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var relativeDeployDirectory = Path.GetRelativePath(fullProjectRoot, fullDeployDirectory)
            .Replace('\\', '/')
            .Trim('/');
        if (!string.IsNullOrWhiteSpace(relativeDeployDirectory))
        {
            yield return $"/{relativeDeployDirectory}/";
        }
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

            yield return line;
        }
    }

    private static void AddIgnorePattern(Matcher matcher, string pattern)
    {
        var normalized = NormalizeIgnorePattern(pattern, out _, out var isRooted);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        AddIgnoreGlobPatterns(matcher, normalized, isRooted);
    }

    private static IgnoreRule CreateIgnoreRule(string pattern)
    {
        var normalized = NormalizeIgnorePattern(pattern, out var isNegation, out var isRooted);
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        AddIgnoreGlobPatterns(matcher, normalized, isRooted);
        return new IgnoreRule(matcher, isNegation, normalized);
    }

    private static string NormalizeIgnorePattern(string pattern, out bool isNegation, out bool isRooted)
    {
        var normalized = pattern.Replace('\\', '/').Trim();
        isNegation = normalized.StartsWith("!", StringComparison.Ordinal);
        if (isNegation)
            normalized = normalized[1..].TrimStart();

        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        isRooted = normalized.StartsWith("/", StringComparison.Ordinal);
        return normalized.TrimStart('/');
    }

    private static void AddIgnoreGlobPatterns(Matcher matcher, string normalized, bool isRooted)
    {
        var patterns = new List<string>();
        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.TrimEnd('/');
            patterns.Add(normalized);
            patterns.Add($"{normalized}/**");
            if (!isRooted)
            {
                patterns.Add($"**/{normalized}");
                patterns.Add($"**/{normalized}/**");
            }
        }
        else
        {
            patterns.Add(normalized);
            if (!isRooted && !normalized.Contains('/'))
            {
                patterns.Add($"**/{normalized}");
            }
        }

        foreach (var candidate in patterns)
        {
            matcher.AddInclude(candidate);
        }
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

    private static bool IsIgnored(
        (Matcher Mandatory, IReadOnlyList<IgnoreRule> Project) matchers,
        string relativePath,
        bool isDirectory = false)
    {
        if (matchers.Mandatory.Match(relativePath).HasMatches)
            return true;

        if (isDirectory && matchers.Mandatory.Match(relativePath + "/").HasMatches)
            return true;

        return EvaluateProjectRules(matchers.Project, relativePath, isDirectory).IsIgnored;
    }

    private static (bool IsIgnored, int LastMatchIndex) EvaluateProjectRules(
        IReadOnlyList<IgnoreRule> rules,
        string relativePath,
        bool isDirectory)
    {
        var isIgnored = false;
        var lastMatchIndex = -1;
        for (var i = 0; i < rules.Count; i++)
        {
            if (!rules[i].Matcher.Match(relativePath).HasMatches
                && (!isDirectory || !rules[i].Matcher.Match(relativePath + "/").HasMatches))
            {
                continue;
            }

            isIgnored = !rules[i].IsNegation;
            lastMatchIndex = i;
        }

        return (isIgnored, lastMatchIndex);
    }

    private static bool CanLaterNegationReopenDirectory(
        IReadOnlyList<IgnoreRule> rules,
        int lastMatchIndex,
        string relativeDirectory)
    {
        var directoryPrefix = relativeDirectory.TrimEnd('/') + "/";
        for (var i = lastMatchIndex + 1; i < rules.Count; i++)
        {
            if (!rules[i].IsNegation)
                continue;

            var pattern = rules[i].Pattern.TrimEnd('/');
            if (pattern.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)
                || pattern.StartsWith("**/", StringComparison.Ordinal)
                || pattern.Split('/')[0].IndexOfAny(['*', '?', '[']) >= 0)
            {
                return true;
            }
        }

        return false;
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
