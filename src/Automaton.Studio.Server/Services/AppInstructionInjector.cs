using System.Text;
using Automaton.Studio.Server.Config;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Options;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Reads .instructions/ files from an app's directory and injects them into the system prompt.
///
/// Injection rules:
///   - _index.md is always injected (if it exists) — it's the summary file, kept short
///   - Files with context: always in their YAML frontmatter are injected in full
///   - Files with context: on-request are NOT injected — the AI reads them via read_instruction
///   - Files with applyTo: "glob,glob" are only injected when the app contains files matching those globs
///   - Hard cap of 1000 tokens (approximated as 4 chars/token) total across all injected files
///   - Files are prioritized: high → medium → low (frontmatter priority field)
/// </summary>
public class AppInstructionInjector
{
    private const int CharsPerToken = 4;

    private readonly AppFileStorageService _storage;
    private readonly AppInstructionsConfig _config;
    private readonly Serilog.ILogger _logger;

    public AppInstructionInjector(AppFileStorageService storage, IOptions<AiProvidersConfig> options)
    {
        _storage = storage;
        _config  = options.Value.AppInstructions;
        _logger  = Log.ForContext<AppInstructionInjector>();
    }

    /// <summary>
    /// Reads .instructions/ files for the given app and returns an injection block
    /// to prepend to the system prompt, or null if no instructions exist.
    /// </summary>
    public async Task<string?> BuildInjectionBlockAsync(Guid userId, Guid appId)
    {
        var appDir = _storage.GetAppDirectoryPath(userId, appId);
        var instructionsDir = Path.Combine(appDir, ".instructions");

        if (!Directory.Exists(instructionsDir))
            return null;

        var files = Directory.GetFiles(instructionsDir, "*.md", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            return null;

        // Build a relative-path list of all non-instruction files in the app directory
        // so we can evaluate applyTo glob patterns against them.
        var appFiles = Directory
            .GetFiles(appDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(appDir, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith(".instructions/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Always inject _index.md first (it's a short summary)
        var ordered = new List<(int priority, string path, string filename)>();

        foreach (var file in files)
        {
            var filename = Path.GetFileName(file);
            var content  = await File.ReadAllTextAsync(file);
            var meta     = ParseFrontmatter(content);

            // _index.md always gets injected with highest priority
            if (filename.Equals("_index.md", StringComparison.OrdinalIgnoreCase))
            {
                ordered.Insert(0, (0, file, filename));
                continue;
            }

            var context = meta.GetValueOrDefault("context", "always");
            if (!context.Equals("always", StringComparison.OrdinalIgnoreCase))
                continue; // on-request files are skipped here

            // applyTo: comma-separated glob patterns — only inject when the app contains matching files
            if (meta.TryGetValue("applyTo", out var applyTo) && !string.IsNullOrWhiteSpace(applyTo))
            {
                if (!MatchesAnyFile(applyTo, appFiles))
                {
                    _logger.Debug("Skipping {File}: applyTo '{ApplyTo}' matched no app files", filename, applyTo);
                    continue;
                }
            }

            var priorityStr = meta.GetValueOrDefault("priority", "medium");
            var priority = priorityStr.ToLower() switch
            {
                "high"   => 1,
                "medium" => 2,
                "low"    => 3,
                _        => 2
            };

            ordered.Add((priority, file, filename));
        }

        ordered = ordered.OrderBy(x => x.priority).ToList();

        if (ordered.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("## User Instructions");
        sb.AppendLine();
        sb.AppendLine("The following instructions have been set for this project. Follow them consistently:");
        sb.AppendLine();

        var remaining = _config.MaxTokens * CharsPerToken;

        foreach (var (_, path, filename) in ordered)
        {
            if (remaining <= 0) break;

            var content  = await File.ReadAllTextAsync(path);
            var stripped = StripFrontmatter(content).Trim();

            if (stripped.Length == 0) continue;

            var chunk = stripped.Length <= remaining
                ? stripped
                : stripped[..remaining] + "\n_(truncated due to token budget)_";

            sb.AppendLine($"### {filename}");
            sb.AppendLine(chunk);
            sb.AppendLine();

            remaining -= stripped.Length;
        }

        var result = sb.ToString().Trim();
        if (result.Length < 30) // Nothing meaningful
            return null;

        _logger.Debug("Injecting {Chars} chars of instructions for app {AppId}", result.Length, appId);
        return result;
    }

    // ── Glob matching ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if any file in <paramref name="relativeFilePaths"/> matches at least one
    /// glob pattern in the comma-separated <paramref name="applyToPatterns"/> string.
    /// Uses <see cref="Microsoft.Extensions.FileSystemGlobbing.Matcher"/> — same engine as MSBuild.
    /// </summary>
    private static bool MatchesAnyFile(string applyToPatterns, IEnumerable<string> relativeFilePaths)
    {
        var patterns = applyToPatterns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
            matcher.AddInclude(pattern);

        return relativeFilePaths.Any(f => matcher.Match(f).HasMatches);
    }

    // ── YAML frontmatter parsing ─────────────────────────────────────────────

    private static Dictionary<string, string> ParseFrontmatter(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines  = content.Split('\n');

        if (lines.Length < 2 || lines[0].Trim() != "---")
            return result;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line == "---") break;

            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            var key   = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim().Trim('"', '\'');
            result[key] = value;
        }

        return result;
    }

    private static string StripFrontmatter(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---")
            return content;

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
                return string.Join('\n', lines[(i + 1)..]);
        }

        return content;
    }
}
