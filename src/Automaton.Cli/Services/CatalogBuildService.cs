using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Cli.Services;

/// <summary>
/// Builds catalog.json, the index an Automaton server reads from an apps repository, from catalog-apps.json and each
/// app's automaton.json. Each app is pinned to the last commit that changed its folder, so an install always gets
/// the files that were listed (and the entry never points at the commit that updates catalog.json).
/// </summary>
internal static partial class CatalogBuildService
{
    internal const int SchemaVersion = 1;
    internal const string ConfigFileName = "catalog-apps.json";
    internal const string CatalogFileName = "catalog.json";

    [GeneratedRegex(@"^[0-9a-f]{40}$")] private static partial Regex Sha();
    [GeneratedRegex(@"^\d+\.\d+\.\d+$")] private static partial Regex Version();
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$")] private static partial Regex Folder();
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")] private static partial Regex Repo();

    internal sealed record Entry(string Id, string Name, string Summary, string Category, string Folder, string Commit, string MinServerVersion, IReadOnlyList<string> Permissions);

    /// <summary>The catalog text, or the problems that stop it from being valid.</summary>
    internal static (string? Json, IReadOnlyList<string> Problems) Build(string directory, string? repoOverride, Func<string, string>? pinnedCommit = null)
    {
        var problems = new List<string>();
        var configPath = Path.Combine(directory, ConfigFileName);
        if (!File.Exists(configPath)) return (null, [$"{ConfigFileName} was not found in {directory}. Run automaton catalog init, or create it."]);

        JsonElement config;
        try { config = JsonDocument.Parse(File.ReadAllText(configPath)).RootElement; }
        catch (JsonException exception) { return (null, [$"{ConfigFileName} is not valid JSON: {exception.Message}"]); }

        var repo = repoOverride ?? Text(config, "repo") ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (repo is null || !Repo().IsMatch(repo))
            problems.Add("The repository is unknown: pass --repo owner/name, or set \"repo\" in " + ConfigFileName + ".");

        var pin = pinnedCommit ?? (folder => LastCommit(directory, folder));
        var entries = new List<Entry>();
        if (!config.TryGetProperty("apps", out var apps) || apps.ValueKind != JsonValueKind.Array)
            problems.Add($"{ConfigFileName} needs an \"apps\" list.");
        else
            foreach (var item in apps.EnumerateArray())
                AddEntry(directory, item, pin, entries, problems);

        problems.AddRange(Problems(entries));
        if (problems.Count > 0) return (null, problems);

        var catalog = new
        {
            schemaVersion = SchemaVersion,
            repo,
            apps = entries.Select(e => new { id = e.Id, name = e.Name, summary = e.Summary, category = e.Category, folder = e.Folder, commit = e.Commit, minServerVersion = e.MinServerVersion, permissions = e.Permissions })
        };
        return (JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }) + "\n", []);
    }

    private static void AddEntry(string directory, JsonElement item, Func<string, string> pin, List<Entry> entries, List<string> problems)
    {
        var folder = Text(item, "folder") ?? "";
        if (!IsAllowedFolder(folder)) { problems.Add($"'{folder}': the folder name is not allowed."); return; }
        var manifestPath = Path.Combine(directory, folder, "automaton.json");
        if (!File.Exists(manifestPath)) { problems.Add($"{folder}: no automaton.json in that folder."); return; }
        try
        {
            var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement;
            entries.Add(new Entry(folder.Split('/').Last(), Text(manifest, "name") ?? "", Text(manifest, "summary") ?? "",
                Text(item, "category") ?? "", folder, pin(folder), Text(item, "minServerVersion") ?? "", Permissions(manifest)));
        }
        catch (JsonException exception) { problems.Add($"{folder}: automaton.json is not valid JSON: {exception.Message}"); }
    }

    internal static IEnumerable<string> Problems(IReadOnlyList<Entry> entries)
    {
        if (entries.Select(e => e.Id).Distinct().Count() != entries.Count) yield return "Two apps have the same folder name (the last part of the folder is the app id).";
        foreach (var e in entries)
        {
            if (!Sha().IsMatch(e.Commit)) yield return $"{e.Id}: no pinned commit (is the folder committed in git?).";
            if (!Version().IsMatch(e.MinServerVersion)) yield return $"{e.Id}: minServerVersion must look like 1.2.3.";
            foreach (var (field, value) in new[] { ("name", e.Name), ("summary", e.Summary), ("category", e.Category) })
                if (string.IsNullOrWhiteSpace(value)) yield return $"{e.Id}: {field} is empty (name and summary come from the app's automaton.json).";
        }
    }

    private static bool IsAllowedFolder(string folder) =>
        Folder().IsMatch(folder) && folder.Length <= 300 && !folder.Split('/').Any(p => p is "." or "..");

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> Permissions(JsonElement manifest) =>
        manifest.TryGetProperty("application", out var application) && application.TryGetProperty("permissions", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).ToList()
            : [];

    private static string LastCommit(string directory, string folder)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "log", "-1", "--format=%H", "--", folder }) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "";
        }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }
}
