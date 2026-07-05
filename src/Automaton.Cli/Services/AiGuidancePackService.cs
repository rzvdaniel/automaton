using Automaton.AiGuidance;
using Automaton.Cli.Models;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Automaton.Cli.Services;

public sealed class AiGuidancePackService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AiGuidanceSyncResult Sync(
        string directory,
        IReadOnlyList<GuidanceSource> references,
        string adapter = AiGuidanceAdapters.None)
    {
        if (!AiGuidanceAdapters.IsKnown(adapter))
        {
            throw new ArgumentException($"Unknown adapter '{adapter}'. Use one of: none, claude, copilot, github-skills, all.", nameof(adapter));
        }

        var mode = DetectMode(directory);
        var generatedFiles = new List<string>();
        var agentsRoot = Path.Combine(directory, ".automaton", "agents");
        Directory.CreateDirectory(agentsRoot);

        var sources = references;
        var variables = BuildTemplateVariables(mode);
        foreach (var source in sources)
        {
            var targetPath = ResolveOutputPath(directory, source.RelativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.WriteAllText(targetPath, RenderVariables(source.Content, variables), Encoding.UTF8);
            generatedFiles.Add(targetPath);
        }
        DeleteLegacyAiGuidance(directory);

        var manifestPath = Path.Combine(agentsRoot, "manifest.json");
        generatedFiles.Add(manifestPath);
        File.WriteAllText(manifestPath, BuildCanonicalManifest(
            mode,
            sources,
            generatedFiles.Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/')).ToList()), Encoding.UTF8);

        return new AiGuidanceSyncResult
        {
            Mode = mode,
            ManifestPath = manifestPath,
            GeneratedFiles = generatedFiles
        };
    }

    private static string DetectMode(string directory)
    {
        if (File.Exists(Path.Combine(directory, "automaton.json")))
        {
            return AiGuidanceModes.App;
        }

        var directoryInfo = new DirectoryInfo(directory);
        if (string.Equals(directoryInfo.Name, "apps", StringComparison.OrdinalIgnoreCase))
        {
            return AiGuidanceModes.Workspace;
        }

        var childDirectories = directoryInfo.Exists ? directoryInfo.GetDirectories() : [];
        if (childDirectories.Any(child => File.Exists(Path.Combine(child.FullName, "automaton.json"))))
        {
            return AiGuidanceModes.Workspace;
        }

        return AiGuidanceModes.Project;
    }

    private static IReadOnlyDictionary<string, string> BuildTemplateVariables(string mode)
    {
        var scopeLine = mode switch
        {
            AiGuidanceModes.App => "This folder is treated as one Automaton app root.",
            AiGuidanceModes.Workspace => "This folder is treated as an Automaton apps workspace; apply the app contract inside each app subfolder.",
            _ => "This folder is treated as a local Automaton project root until an app is created or linked here."
        };

        var modeDescription = mode switch
        {
            AiGuidanceModes.App => "one Automaton app root",
            AiGuidanceModes.Workspace => "an Automaton apps workspace with one or more app subfolders",
            _ => "a local project root that may later become an Automaton app root"
        };

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = mode,
            ["scopeLine"] = scopeLine,
            ["modeDescription"] = modeDescription,
            ["title"] = "Automaton"
        };
    }

    private static string RenderVariables(string content, IReadOnlyDictionary<string, string> variables)
    {
        var rendered = content;
        foreach (var variable in variables)
        {
            rendered = rendered.Replace("{{" + variable.Key + "}}", variable.Value, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string ComputeHash(IEnumerable<string> parts)
    {
        var combined = string.Join("\n---\n", parts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void DeleteLegacyGeneratedAdapters(string directory)
    {
        DeleteIfGenerated(Path.Combine(directory, "CLAUDE.md"));
        DeleteIfGenerated(Path.Combine(directory, ".github", "copilot-instructions.md"));
        DeleteIfGenerated(Path.Combine(directory, ".github", "skills", "automaton-app", "SKILL.md"));

        TryDeleteEmptyDirectory(Path.Combine(directory, ".github", "skills", "automaton-app"));
        TryDeleteEmptyDirectory(Path.Combine(directory, ".github", "skills"));
        TryDeleteEmptyDirectory(Path.Combine(directory, ".github"));
    }

    private static void DeleteLegacyAiGuidance(string directory)
    {
        DeleteLegacyGeneratedAdapters(directory);
        var legacyRoot = Path.Combine(directory, ".automaton", "ai-guidance");
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
        {
            File.Delete(path);
        }

        DeleteLegacyManifest(Path.Combine(legacyRoot, "manifest.json"));
        TryDeleteEmptyDirectory(Path.Combine(legacyRoot, "llms"));
        TryDeleteEmptyDirectory(legacyRoot);
    }

    private static void DeleteLegacyManifest(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("generator", out var generator)
                && generator.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "Automaton.Cli", StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }
        catch (JsonException)
        {
            // Leave unrecognized user files in place.
        }
    }

    private static void DeleteIfGenerated(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var content = File.ReadAllText(path);
        if (content.Contains("This file is generated by `automaton`", StringComparison.Ordinal))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }

    private static string ResolveOutputPath(string directory, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || Path.IsPathRooted(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains("../", StringComparison.Ordinal)
            || normalized.Contains("..\\", StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"AI guidance export path '{relativePath}' is not a safe relative path.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(directory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var fullRoot = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AI guidance export path '{relativePath}' escapes the project root.");
        }

        return fullPath;
    }

    private static string BuildCanonicalManifest(
        string mode,
        IReadOnlyList<GuidanceSource> sources,
        IReadOnlyList<string> generatedFiles)
    {
        var manifest = new
        {
            version = 1,
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            mode,
            canonicalRoot = ".automaton/agents",
            sourceHash = ComputeHash(sources.Select(source => source.RelativePath + "\n" + source.Content)),
            sources = sources.Select(source => source.RelativePath).ToList(),
            generatedFiles,
            entrypoints = new
            {
                root = "AGENTS.md",
                skill = ".automaton/agents/skills/automaton-app/SKILL.md",
                references = ".automaton/agents/references/llms/"
            },
            adapters = new[]
            {
                new { tool = "generic", path = "AGENTS.md" }
            },
            generator = new
            {
                name = "Automaton.Cli",
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
            }
        };

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }
}
