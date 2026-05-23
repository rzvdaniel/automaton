using Automaton.Cli.Models;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Automaton.Cli.Services;

public sealed class AiGuidancePackService
{
    private const string ResourcePrefix = "Automaton.Cli.AiGuidance.";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, string> ResourceMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["llms.txt"] = "llms.txt",
        ["llms.automaton-cli-install.md"] = "llms/automaton-cli-install.md",
        ["llms.automaton-apps-overview.md"] = "llms/automaton-apps-overview.md",
        ["llms.automaton-backend-controllers.md"] = "llms/automaton-backend-controllers.md",
        ["llms.automaton-cli-deploy.md"] = "llms/automaton-cli-deploy.md",
        ["llms.automaton-frontend-structure.md"] = "llms/automaton-frontend-structure.md",
        ["llms.automaton-starter-prompts.md"] = "llms/automaton-starter-prompts.md"
    };

    private static readonly IReadOnlyDictionary<string, string> TemplateMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["templates.AGENTS.md"] = "AGENTS.md",
        ["templates.instructions.md"] = "instructions.md",
        ["templates.SKILL.md"] = "SKILL.md",
        ["templates.adapter.md"] = "adapter.md",
        ["templates.github-skill.md"] = "github-skill.md"
    };

    public AiGuidanceSyncResult Sync(string directory, string adapter = AiGuidanceAdapters.None)
    {
        if (!AiGuidanceAdapters.IsKnown(adapter))
        {
            throw new ArgumentException($"Unknown adapter '{adapter}'. Use one of: none, claude, copilot, github-skills, all.", nameof(adapter));
        }

        var mode = DetectMode(directory);
        var generatedFiles = new List<string>();
        var agentsRoot = Path.Combine(directory, ".automaton", "agents");
        var referencesRoot = Path.Combine(agentsRoot, "references");
        Directory.CreateDirectory(agentsRoot);
        Directory.CreateDirectory(referencesRoot);

        var sources = LoadSources(ResourceMap);
        var templates = LoadSources(TemplateMap);
        var variables = BuildTemplateVariables(mode);
        foreach (var source in sources)
        {
            var targetPath = Path.Combine(referencesRoot, source.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.WriteAllText(targetPath, source.Content, Encoding.UTF8);
            generatedFiles.Add(targetPath);
        }

        var canonicalInstructionsPath = Path.Combine(agentsRoot, "instructions.md");
        File.WriteAllText(canonicalInstructionsPath, RenderTemplate(templates, "instructions.md", variables), Encoding.UTF8);
        generatedFiles.Add(canonicalInstructionsPath);

        var canonicalSkillsRoot = Path.Combine(agentsRoot, "skills", "automaton-app");
        Directory.CreateDirectory(canonicalSkillsRoot);

        var canonicalSkillPath = Path.Combine(canonicalSkillsRoot, "SKILL.md");
        File.WriteAllText(canonicalSkillPath, RenderTemplate(templates, "SKILL.md", variables), Encoding.UTF8);
        generatedFiles.Add(canonicalSkillPath);

        var agentsPath = Path.Combine(directory, "AGENTS.md");
        File.WriteAllText(agentsPath, RenderTemplate(templates, "AGENTS.md", variables), Encoding.UTF8);
        generatedFiles.Add(agentsPath);

        SyncOptionalAdapters(directory, adapter, generatedFiles, templates, variables);
        DeleteLegacyAiGuidance(directory);

        var manifestPath = Path.Combine(agentsRoot, "manifest.json");
        generatedFiles.Add(manifestPath);
        File.WriteAllText(manifestPath, BuildCanonicalManifest(
            mode,
            sources.Concat(templates).ToList(),
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

    private static IReadOnlyList<(string RelativePath, string Content)> LoadSources(IReadOnlyDictionary<string, string> resourceMap)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return resourceMap
            .Select(entry =>
            {
                var resourceName = ResourcePrefix + entry.Key;
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Embedded AI guidance resource '{resourceName}' was not found.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return (entry.Value, reader.ReadToEnd());
            })
            .ToList();
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
            ["modeDescription"] = modeDescription
        };
    }

    private static IReadOnlyDictionary<string, string> WithVariable(
        IReadOnlyDictionary<string, string> variables,
        string key,
        string value)
    {
        var copy = new Dictionary<string, string>(variables, StringComparer.Ordinal)
        {
            [key] = value
        };
        return copy;
    }

    private static string RenderTemplate(
        IReadOnlyList<(string RelativePath, string Content)> templates,
        string relativePath,
        IReadOnlyDictionary<string, string> variables)
    {
        var template = templates.FirstOrDefault(source => string.Equals(source.RelativePath, relativePath, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(template.RelativePath))
        {
            throw new InvalidOperationException($"Embedded AI guidance template '{relativePath}' was not found.");
        }

        var rendered = template.Content;
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

    private static void SyncOptionalAdapters(
        string directory,
        string adapter,
        ICollection<string> generatedFiles,
        IReadOnlyList<(string RelativePath, string Content)> templates,
        IReadOnlyDictionary<string, string> variables)
    {
        if (string.Equals(adapter, AiGuidanceAdapters.None, StringComparison.OrdinalIgnoreCase))
        {
            DeleteLegacyGeneratedAdapters(directory);
            return;
        }

        if (string.Equals(adapter, AiGuidanceAdapters.Claude, StringComparison.OrdinalIgnoreCase)
            || string.Equals(adapter, AiGuidanceAdapters.All, StringComparison.OrdinalIgnoreCase))
        {
            var claudePath = Path.Combine(directory, "CLAUDE.md");
            File.WriteAllText(claudePath, RenderTemplate(
                templates,
                "adapter.md",
                WithVariable(variables, "title", "CLAUDE.md")), Encoding.UTF8);
            generatedFiles.Add(claudePath);
        }
        else
        {
            DeleteIfGenerated(Path.Combine(directory, "CLAUDE.md"));
        }

        if (string.Equals(adapter, AiGuidanceAdapters.Copilot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(adapter, AiGuidanceAdapters.All, StringComparison.OrdinalIgnoreCase))
        {
            var copilotPath = Path.Combine(directory, ".github", "copilot-instructions.md");
            Directory.CreateDirectory(Path.GetDirectoryName(copilotPath)!);
            File.WriteAllText(copilotPath, RenderTemplate(
                templates,
                "adapter.md",
                WithVariable(variables, "title", "GitHub Copilot")), Encoding.UTF8);
            generatedFiles.Add(copilotPath);
        }
        else
        {
            DeleteIfGenerated(Path.Combine(directory, ".github", "copilot-instructions.md"));
        }

        if (string.Equals(adapter, AiGuidanceAdapters.GitHubSkills, StringComparison.OrdinalIgnoreCase)
            || string.Equals(adapter, AiGuidanceAdapters.All, StringComparison.OrdinalIgnoreCase))
        {
            var skillPath = Path.Combine(directory, ".github", "skills", "automaton-app", "SKILL.md");
            Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
            File.WriteAllText(skillPath, RenderTemplate(templates, "github-skill.md", variables), Encoding.UTF8);
            generatedFiles.Add(skillPath);
        }
        else
        {
            DeleteIfGenerated(Path.Combine(directory, ".github", "skills", "automaton-app", "SKILL.md"));
        }

        TryDeleteEmptyDirectory(Path.Combine(directory, ".github", "skills", "automaton-app"));
        TryDeleteEmptyDirectory(Path.Combine(directory, ".github", "skills"));
        TryDeleteEmptyDirectory(Path.Combine(directory, ".github"));
    }

    private static void DeleteLegacyAiGuidance(string directory)
    {
        var legacyRoot = Path.Combine(directory, ".automaton", "ai-guidance");
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        foreach (var source in ResourceMap.Values)
        {
            var path = Path.Combine(legacyRoot, source.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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

    private static string BuildCanonicalManifest(
        string mode,
        IReadOnlyList<(string RelativePath, string Content)> sources,
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
                instructions = ".automaton/agents/instructions.md",
                skill = ".automaton/agents/skills/automaton-app/SKILL.md",
                references = ".automaton/agents/references/llms.txt"
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
