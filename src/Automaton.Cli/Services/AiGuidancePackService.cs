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
        Directory.CreateDirectory(guidanceRoot);

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

        var agentsPath = Path.Combine(directory, "AGENTS.md");
        File.WriteAllText(agentsPath, BuildAgentsContent(mode), Encoding.UTF8);
        generatedFiles.Add(agentsPath);

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

This file is generated by `automaton` from the canonical Automaton `llms` sources. Do not edit it manually.

{{scopeLine}}

Source of truth in this folder:

- `.automaton/ai-guidance/llms.txt`
- `.automaton/ai-guidance/llms/automaton-cli-deploy.md`
- `.automaton/ai-guidance/llms/automaton-apps-overview.md`
- `.automaton/ai-guidance/llms/automaton-frontend-structure.md`
- `.automaton/ai-guidance/llms/automaton-backend-controllers.md`
- `.automaton/ai-guidance/llms/automaton-starter-prompts.md`

Working rules:

- Read `.automaton/ai-guidance/llms.txt` first.
- If any local prompt or stale note conflicts with these generated files, prefer the generated files.
- Run `automaton sync-ai` to refresh this local guidance pack after a CLI upgrade or if the generated files were removed.
- Keep `automaton.json` in each app root.
- Use `automaton setup`, `automaton new`, `automaton link`, and `automaton deploy` as the default CLI flow.
- For backend apps, pick an existing connected runner or tell the user to create or deploy one in Automaton Studio.
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

This file is generated by `automaton` from the canonical Automaton `llms` sources. Do not edit it manually.

{{modeRule}}

- Start with `.automaton/ai-guidance/llms.txt`.
- Read the linked local files under `.automaton/ai-guidance/llms/` before making structure or deploy decisions.
- If the generated local guidance pack looks stale or is missing, run `automaton sync-ai`.
- Keep `automaton.json` at the app root.
- For backend apps, select an existing connected runner with `automaton set-runner <runner-id>`, or tell the user to create or deploy one in Automaton Studio if none exists.
- Use `automaton deploy` as the shipping command after validation.
""";
    }

    private static string BuildSkillContent(string mode)
    {
        return $$"""
---
name: automaton-app
description: Local Automaton app guidance generated from the canonical llms sources.
---

# Automaton App

This file is generated by `automaton`. Do not edit it manually.

Mode: `{{mode}}`

## Read First

1. `.automaton/ai-guidance/llms.txt`
2. `.automaton/ai-guidance/llms/automaton-cli-deploy.md`
3. `.automaton/ai-guidance/llms/automaton-apps-overview.md`
4. `.automaton/ai-guidance/llms/automaton-frontend-structure.md`
5. `.automaton/ai-guidance/llms/automaton-backend-controllers.md`
6. `.automaton/ai-guidance/llms/automaton-starter-prompts.md`

## Operating Rules

- Follow the generated local `llms` copies as the source of truth for Automaton app structure.
- Run `automaton sync-ai` to refresh these local guidance files when needed.
- Keep `automaton.json` in the app root and commit it.
- Keep frontend-only apps simple; add `backend/` only when the app truly needs Python controllers.
- For backend apps, use an existing connected runner or tell the user to create/deploy one in Automaton Studio.
- Prefer the intent-first CLI flow: `automaton setup`, `automaton new` or `automaton link`, then `automaton deploy`.
""";
    }
}