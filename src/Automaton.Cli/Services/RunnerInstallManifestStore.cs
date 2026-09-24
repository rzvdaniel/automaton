using Automaton.Cli.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Cli.Services;

/// <summary>Reads and writes the local Runner install records and active install marker.</summary>
internal sealed class RunnerInstallManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string RunnerHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".automaton", "runner");

    internal string InstallsDirectory => Path.Combine(RunnerHome, "installs");

    internal RunnerInstallRecord? Load(string safeRunnerName)
    {
        var path = GetInstallRecordPath(safeRunnerName);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RunnerInstallRecord>(json, JsonOptions);
    }

    internal void Save(RunnerInstallRecord record)
    {
        Directory.CreateDirectory(InstallsDirectory);
        File.WriteAllText(
            GetInstallRecordPath(record.SafeRunnerName),
            JsonSerializer.Serialize(record, JsonOptions));
    }

    internal string GetInstallRecordPath(string safeRunnerName)
        => Path.Combine(InstallsDirectory, $"{safeRunnerName}.json");

    internal void SetActiveRunner(string safeRunnerName)
    {
        Directory.CreateDirectory(RunnerHome);
        File.WriteAllText(ActiveRunnerPath, safeRunnerName);
    }

    internal string? ReadActiveRunnerSafeName()
        => File.Exists(ActiveRunnerPath) ? File.ReadAllText(ActiveRunnerPath).Trim() : null;

    internal void ClearActiveRunner()
    {
        if (File.Exists(ActiveRunnerPath)) File.Delete(ActiveRunnerPath);
    }

    private string ActiveRunnerPath => Path.Combine(RunnerHome, "active-runner.txt");
}
