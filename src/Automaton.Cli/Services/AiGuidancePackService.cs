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

        var sources = LoadSources();
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
        File.WriteAllText(canonicalInstructionsPath, BuildCanonicalInstructions(mode), Encoding.UTF8);
        generatedFiles.Add(canonicalInstructionsPath);

        var canonicalSkillsRoot = Path.Combine(agentsRoot, "skills", "automaton-app");
        Directory.CreateDirectory(canonicalSkillsRoot);

        var canonicalSkillPath = Path.Combine(canonicalSkillsRoot, "SKILL.md");
        File.WriteAllText(canonicalSkillPath, BuildCanonicalSkillContent(mode), Encoding.UTF8);
        generatedFiles.Add(canonicalSkillPath);

        var agentsPath = Path.Combine(directory, "AGENTS.md");
        File.WriteAllText(agentsPath, BuildAgentsContent(mode), Encoding.UTF8);
        generatedFiles.Add(agentsPath);

        SyncOptionalAdapters(directory, mode, adapter, generatedFiles);
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

    private static IReadOnlyList<(string RelativePath, string Content)> LoadSources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return ResourceMap
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
        string mode,
        string adapter,
        ICollection<string> generatedFiles)
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
            File.WriteAllText(claudePath, BuildClaudeAdapter(mode), Encoding.UTF8);
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
            File.WriteAllText(copilotPath, BuildCopilotAdapter(mode), Encoding.UTF8);
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
            File.WriteAllText(skillPath, BuildGitHubSkillAdapter(mode), Encoding.UTF8);
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

    private static string BuildAgentsContent(string mode)
    {
        var scopeLine = mode switch
        {
            AiGuidanceModes.App => "This folder is treated as one Automaton app root.",
            AiGuidanceModes.Workspace => "This folder is treated as an Automaton apps workspace; apply the app contract inside each app subfolder.",
            _ => "This folder is treated as a local Automaton project root until an app is created or linked here."
        };

        return $$"""
# Automaton Local Guidance

This file is generated by `automaton`. Do not edit it manually.

{{scopeLine}}

Canonical local guidance:

- `.automaton/agents/instructions.md`
- `.automaton/agents/skills/automaton-app/SKILL.md`
- `.automaton/agents/references/llms.txt`

Working rules:

- Read `.automaton/agents/instructions.md` first.
- Then read `.automaton/agents/skills/automaton-app/SKILL.md`.
- Use `.automaton/agents/references/llms.txt` and the linked files for the detailed Automaton contract.
- If any local prompt or stale note conflicts with these generated files, prefer the generated files.
- Run `automaton sync-ai` to refresh this local guidance pack after a CLI upgrade or if the generated files were removed.
- Keep `automaton.json` in each app root.
- Use `automaton setup`, `automaton new`, `automaton link`, and `automaton deploy` as the default CLI flow.
- For backend apps, let `automaton deploy` choose or prompt for a connected runner; if none exists, tell the user to create or deploy one in Automaton Studio.
""";
    }

    private static string BuildClaudeAdapter(string mode)
    {
        return BuildAdapterMarkdown("CLAUDE.md", mode);
    }

    private static string BuildCopilotAdapter(string mode)
    {
        return BuildAdapterMarkdown("GitHub Copilot", mode);
    }

    private static string BuildAdapterMarkdown(string title, string mode)
    {
        var scopeLine = mode switch
        {
            AiGuidanceModes.App => "Treat this folder as the Automaton app root.",
            AiGuidanceModes.Workspace => "Treat this folder as an Automaton apps workspace and apply the app contract inside each app subfolder.",
            _ => "Treat this folder as the current local Automaton project root."
        };

        return $$"""
# Automaton Local Guidance for {{title}}

This file is generated by `automaton`. Do not edit it manually.

{{scopeLine}}

Read first:

1. `.automaton/agents/instructions.md`
2. `.automaton/agents/skills/automaton-app/SKILL.md`
3. `.automaton/agents/references/llms.txt`

If these files are missing or stale, run `automaton sync-ai`.
""";
    }

    private static string BuildGitHubSkillAdapter(string mode)
    {
        return $$"""
---
name: automaton-app
description: Thin compatibility wrapper around the canonical local Automaton guidance.
---

# Automaton App

This file is generated by `automaton`. Do not edit it manually.

Mode: `{{mode}}`

Read first:

1. `.automaton/agents/instructions.md`
2. `.automaton/agents/skills/automaton-app/SKILL.md`
3. `.automaton/agents/references/llms.txt`

If these files are missing or stale, run `automaton sync-ai`.
""";
    }

    private static string BuildCanonicalInstructions(string mode)
    {
        var modeDescription = mode switch
        {
            AiGuidanceModes.App => "one Automaton app root",
            AiGuidanceModes.Workspace => "an Automaton apps workspace with one or more app subfolders",
            _ => "a local project root that may later become an Automaton app root"
        };

        return $$"""
# Automaton Agent Instructions

This file is generated by `automaton` as the tool-neutral local guidance entrypoint. Do not edit it manually.

## Purpose

Help an AI tool work correctly in this folder as {{modeDescription}}.

## Read Order

1. Read `.automaton/agents/skills/automaton-app/SKILL.md`.
2. Read `.automaton/agents/references/llms.txt`.
3. Read the linked files under `.automaton/agents/references/llms/` before making structure, backend, or deploy decisions.
4. Use `AGENTS.md` only as the root discovery entrypoint, not as the canonical source.

## Core Rules

- Prefer the generated local Automaton guidance over stale prompts or handwritten notes.
- Keep `automaton.json` in each app root.
- Use the CLI as the primary execution path.
- Start with `automaton setup`.
- Use `automaton new` or `automaton link` to create or connect the app config when needed.
- Use `automaton deploy` to run the deploy preflight and ship the app.
- Run `automaton validate` only when you need a separate diagnostic check.
- For backend apps, let `automaton deploy` choose or prompt for a connected runner; tell the user to create or deploy one in Automaton Studio only if none exists.

## Compatibility

- `.automaton/agents/` is the canonical local agent guidance tree.
- `AGENTS.md` is the only default root adapter and points back to `.automaton/agents/`.
- Tool-specific adapters such as `CLAUDE.md` or `.github/copilot-instructions.md` are intentionally not generated by default.
""";
    }

    private static string BuildCanonicalSkillContent(string mode)
    {
        return $$"""
---
id: automaton-app
title: Automaton App Workflow
version: 1
mode: {{mode}}
summary: Universal local guidance for building, linking, and deploying Automaton apps.
---

# Automaton App Workflow

This file is generated by `automaton` as canonical reusable guidance for local AI tools.

## When To Use

- The task involves an Automaton app in this folder.
- The task involves `automaton.json`, frontend structure, backend controllers, runner selection, or deploy.

## Inputs Expected

- The current workspace contents.
- `.automaton/agents/references/llms.txt`
- The linked files under `.automaton/agents/references/llms/`

## Workflow

1. Read `.automaton/agents/references/llms.txt` first.
2. Use the linked local guidance files before making decisions about structure, backend controllers, or deploy packaging.
3. Keep `automaton.json` in the app root.
4. Prefer frontend-only structure unless the app truly needs Python backend controllers in `backend/`.
5. Run `automaton deploy`; it performs the deploy preflight and prompts for runner selection only when backend controllers require one.
6. Run `automaton validate` only as an optional diagnostic check when deploy readiness is unclear.

## Validation

- `automaton deploy`
- `automaton validate` only for optional diagnostics.
- Publish only after explicit user approval when approval is needed.
""";
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
