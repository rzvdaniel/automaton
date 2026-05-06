using System.IO.Compression;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Automaton.Cli.Services;

/// <summary>
/// Creates a zip stream from a local directory, respecting an optional deploy directory
/// (dist/ → frontend/ → project root fallback).
/// Files inside .git/, node_modules/, and obj/ are excluded.
/// automaton.json is excluded from the upload (it's local-only).
/// </summary>
public static class ZipService
{
    private const string IgnoreFileName = ".automatonignore";

    public static readonly string[] DefaultIgnorePatterns =
    {
        ".git/",
        "node_modules/",
        "obj/",
        "bin/",
        ".vs/",
        ".idea/",
        "automaton.json",
        ".automatonignore"
    };

    /// <summary>
    /// Resolves which directory to zip: explicit deployDir → dist → frontend → projectRoot.
    /// </summary>
    public static string ResolveDeployDirectory(string projectRoot, string? deployDir)
    {
        if (!string.IsNullOrWhiteSpace(deployDir))
        {
            var explicit_ = Path.Combine(projectRoot, deployDir);
            if (Directory.Exists(explicit_)) return explicit_;
            throw new DirectoryNotFoundException(
                $"deployDir '{deployDir}' configured in automaton.json does not exist: {explicit_}");
        }

        // Auto-detect
        var dist     = Path.Combine(projectRoot, "dist");
        var frontend = Path.Combine(projectRoot, "frontend");

        if (Directory.Exists(dist))     return dist;
        if (Directory.Exists(frontend)) return frontend;
        return projectRoot;
    }

    /// <summary>
    /// Creates an in-memory zip of <paramref name="sourceDir"/> and returns the stream.
    /// The returned stream is positioned at 0 — caller is responsible for disposing.
    /// </summary>
    public static Stream CreateZip(string projectRoot, string sourceDir)
    {
        var ignoreMatcher = CreateIgnoreMatcher(projectRoot);
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddDirectory(archive, sourceDir, sourceDir, ignoreMatcher);
        }

        ms.Position = 0;
        return ms;
    }

    private static void AddDirectory(ZipArchive archive, string rootDir, string currentDir, Matcher ignoreMatcher)
    {
        foreach (var file in Directory.GetFiles(currentDir))
        {
            var entryName = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            if (IsIgnored(ignoreMatcher, entryName)) continue;

            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        foreach (var dir in Directory.GetDirectories(currentDir))
        {
            var relativeDir = Path.GetRelativePath(rootDir, dir).Replace('\\', '/');
            if (IsIgnored(ignoreMatcher, relativeDir, isDirectory: true)) continue;

            AddDirectory(archive, rootDir, dir, ignoreMatcher);
        }
    }

    private static Matcher CreateIgnoreMatcher(string projectRoot)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

        foreach (var pattern in DefaultIgnorePatterns)
        {
            AddIgnorePattern(matcher, pattern);
        }

        foreach (var pattern in LoadCustomIgnorePatterns(projectRoot))
        {
            AddIgnorePattern(matcher, pattern);
        }

        return matcher;
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
}
