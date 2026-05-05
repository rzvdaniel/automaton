using System.IO.Compression;

namespace Automaton.Cli.Services;

/// <summary>
/// Creates a zip stream from a local directory, respecting an optional deploy directory
/// (dist/ → frontend/ → project root fallback).
/// Files inside .git/, node_modules/, and obj/ are excluded.
/// automaton.json is excluded from the upload (it's local-only).
/// </summary>
public static class ZipService
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "obj", "bin", ".vs", ".idea"
    };

    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "automaton.json"
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
    public static Stream CreateZip(string sourceDir)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddDirectory(archive, sourceDir, sourceDir);
        }

        ms.Position = 0;
        return ms;
    }

    private static void AddDirectory(ZipArchive archive, string rootDir, string currentDir)
    {
        foreach (var file in Directory.GetFiles(currentDir))
        {
            var fileName = Path.GetFileName(file);
            if (ExcludedFiles.Contains(fileName)) continue;

            var entryName = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        foreach (var dir in Directory.GetDirectories(currentDir))
        {
            if (ExcludedDirs.Contains(Path.GetFileName(dir))) continue;
            AddDirectory(archive, rootDir, dir);
        }
    }
}
