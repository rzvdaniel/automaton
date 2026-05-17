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

    public AiGuidanceSyncResult Sync(string directory)
    {
        var mode = DetectMode(directory);
        var generatedFiles = new List<string>();
        var guidanceRoot = Path.Combine(directory, ".automaton", "ai-guidance");
        var agentsRoot = Path.Combine(directory, ".automaton", "agents");
        Directory.CreateDirectory(guidanceRoot);
        Directory.CreateDirectory(agentsRoot);

        var sources = LoadSources();
        foreach (var source in sources)
        {
            var targetPath = Path.Combine(guidanceRoot, source.RelativePath.Replace('/', Path.DirectorySeparatorChar));
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

        var canonicalManifestPath = Path.Combine(agentsRoot, "manifest.json");
        File.WriteAllText(canonicalManifestPath, BuildCanonicalManifest(mode), Encoding.UTF8);
        generatedFiles.Add(canonicalManifestPath);

        var agentsPath = Path.Combine(directory, "AGENTS.md");
        File.WriteAllText(agentsPath, BuildAgentsContent(mode), Encoding.UTF8);
        generatedFiles.Add(agentsPath);

        var claudePath = Path.Combine(directory, "CLAUDE.md");
        File.WriteAllText(claudePath, BuildClaudeContent(mode), Encoding.UTF8);
        generatedFiles.Add(claudePath);

        var copilotPath = Path.Combine(directory, ".github", "copilot-instructions.md");
        Directory.CreateDirectory(Path.GetDirectoryName(copilotPath)!);
        File.WriteAllText(copilotPath, BuildCopilotInstructions(mode), Encoding.UTF8);
        generatedFiles.Add(copilotPath);

        var skillPath = Path.Combine(directory, ".github", "skills", "automaton-app", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
        File.WriteAllText(skillPath, BuildSkillContent(mode), Encoding.UTF8);
        generatedFiles.Add(skillPath);

        var manifestPath = Path.Combine(guidanceRoot, "manifest.json");
        var manifest = new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            mode,
            sourceHash = ComputeHash(sources.Select(source => source.RelativePath + "\n" + source.Content)),
            sources = sources.Select(source => source.RelativePath).ToList(),
            generatedFiles = generatedFiles.Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/')).ToList(),
            generator = new
            {
                name = "Automaton.Cli",
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
            }
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        generatedFiles.Add(manifestPath);

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
- `.automaton/ai-guidance/llms.txt`

Working rules:

- Read `.automaton/agents/instructions.md` first.
- Then read `.automaton/agents/skills/automaton-app/SKILL.md`.
- Use `.automaton/ai-guidance/llms.txt` and the linked files for the detailed Automaton contract.
- If any local prompt or stale note conflicts with these generated files, prefer the generated files.
- Run `automaton sync-ai` to refresh this local guidance pack after a CLI upgrade or if the generated files were removed.
- Keep `automaton.json` in each app root.
- Use `automaton setup`, `automaton new`, `automaton link`, and `automaton deploy` as the default CLI flow.
- For backend apps, let `automaton deploy` choose or prompt for a connected runner; if none exists, tell the user to create or deploy one in Automaton Studio.
""";
    }

    private static string BuildClaudeContent(string mode)
    {
        var scopeLine = mode switch
        {
            AiGuidanceModes.App => "Treat this folder as the Automaton app root.",
            AiGuidanceModes.Workspace => "Treat this folder as an Automaton apps workspace and apply the app contract inside each app subfolder.",
            _ => "Treat this folder as the current local Automaton project root."
        };

        return $$"""
# CLAUDE.md

This file is generated by `automaton`. Do not edit it manually.

{{scopeLine}}

Read in this order:

1. `.automaton/agents/instructions.md`
2. `.automaton/agents/skills/automaton-app/SKILL.md`
3. `.automaton/ai-guidance/llms.txt`
4. The linked files under `.automaton/ai-guidance/llms/`

Rules:

- Prefer the generated local Automaton guidance over stale local notes.
- Run `automaton sync-ai` if these files are missing or outdated.
- Keep `automaton.json` at the app root.
- Use the CLI as the primary workflow: `automaton setup`, `automaton new` or `automaton link`, then `automaton deploy`.
- For backend apps, let `automaton deploy` choose or prompt for a connected runner; if none exists, tell the user to create or deploy one in Automaton Studio.
""";
    }

    private static string BuildCopilotInstructions(string mode)
    {
        var modeRule = mode switch
        {
            AiGuidanceModes.Workspace => "Treat this folder as a workspace that may contain multiple Automaton apps in subfolders.",
            AiGuidanceModes.App => "Treat this folder as the Automaton app root.",
            _ => "Treat this folder as the current Automaton project root."
        };

        return $$"""
# Generated Automaton Instructions

This file is generated by `automaton`. Do not edit it manually.

{{modeRule}}

- Start with `.automaton/agents/instructions.md`.
- Then read `.automaton/agents/skills/automaton-app/SKILL.md`.
- Use `.automaton/ai-guidance/llms.txt` and the linked local files before making structure or deploy decisions.
- If the generated local guidance pack looks stale or is missing, run `automaton sync-ai`.
- Keep `automaton.json` at the app root.
- For backend apps, let `automaton deploy` choose or prompt for a connected runner; if none exists, tell the user to create or deploy one in Automaton Studio.
- Use `automaton deploy` as the shipping command; run `automaton validate` only as an optional diagnostic preflight.
""";
    }

    private static string BuildSkillContent(string mode)
    {
        return $$"""
---
name: automaton-app
description: Thin compatibility wrapper around the canonical local Automaton guidance.
---

# Automaton App

This file is generated by `automaton`. Do not edit it manually.

Mode: `{{mode}}`

## Read First

1. `.automaton/agents/instructions.md`
2. `.automaton/agents/skills/automaton-app/SKILL.md`
3. `.automaton/ai-guidance/llms.txt`
4. `.automaton/ai-guidance/llms/automaton-cli-install.md`
5. `.automaton/ai-guidance/llms/automaton-cli-deploy.md`
6. `.automaton/ai-guidance/llms/automaton-apps-overview.md`
7. `.automaton/ai-guidance/llms/automaton-frontend-structure.md`
8. `.automaton/ai-guidance/llms/automaton-backend-controllers.md`
9. `.automaton/ai-guidance/llms/automaton-starter-prompts.md`

## Operating Rules

- Follow the generated local `llms` copies as the source of truth for Automaton app structure.
- Treat this file as a compatibility shim for tools that scan `.github/skills/`.
- Run `automaton sync-ai` to refresh these local guidance files when needed.
- Keep `automaton.json` in the app root and commit it.
- Keep frontend-only apps simple; add `backend/` only when the app truly needs Python controllers.
- For backend apps, let `automaton deploy` choose or prompt for a connected runner; tell the user to create/deploy one in Automaton Studio only if none exists.
- Prefer the intent-first CLI flow: `automaton setup`, `automaton new` or `automaton link`, then `automaton deploy`.
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

1. Read `.automaton/ai-guidance/llms.txt`.
2. Read the linked files under `.automaton/ai-guidance/llms/` before making structure, backend, or deploy decisions.
3. Use tool-specific wrapper files like `AGENTS.md`, `CLAUDE.md`, or `.github/copilot-instructions.md` only as discovery entrypoints, not as the canonical source.

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
- Root files like `AGENTS.md` and `CLAUDE.md` are lightweight wrappers for AI tools that auto-discover those names.
- `.github/skills/` is a compatibility export for tools that look for GitHub-style skill folders.
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
- `.automaton/ai-guidance/llms.txt`
- The linked files under `.automaton/ai-guidance/llms/`

## Workflow

1. Read `.automaton/ai-guidance/llms.txt` first.
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

    private static string BuildCanonicalManifest(string mode)
    {
        var manifest = new
        {
            version = 1,
            mode,
            canonicalRoot = ".automaton/agents",
            entrypoints = new
            {
                instructions = ".automaton/agents/instructions.md",
                skill = ".automaton/agents/skills/automaton-app/SKILL.md",
                llms = ".automaton/ai-guidance/llms.txt"
            },
            adapters = new[]
            {
                new { tool = "codex", path = "AGENTS.md" },
                new { tool = "claude", path = "CLAUDE.md" },
                new { tool = "copilot", path = ".github/copilot-instructions.md" },
                new { tool = "github-skills", path = ".github/skills/automaton-app/SKILL.md" }
            }
        };

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }
}
