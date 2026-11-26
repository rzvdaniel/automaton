using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>automaton import-compose: make an app folder (automaton.json and compose.yaml) from a Docker Compose file.</summary>
public static class ImportComposeCommand
{
    public static Command Build()
    {
        var file = new Argument<string>("compose-file", "Path of the Compose file (compose.yaml)");
        var name = new Option<string?>("--name", "App name (default: the output folder's name)");
        var output = new Option<string?>("--out", "Folder to write automaton.json and compose.yaml into (default: the current folder)");
        var command = new Command("import-compose", "Create automaton.json and a supported compose.yaml from a Docker Compose file; nothing is deployed") { file, name, output };
        command.SetHandler((string path, string? appName, string? outDirectory) =>
        {
            try
            {
                var target = Path.GetFullPath(outDirectory ?? ".");
                var result = ComposeImportService.Import(File.ReadAllText(path), appName ?? Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar)));
                var manifestPath = Path.Combine(target, "automaton.json");
                var composePath = Path.Combine(target, "compose.yaml");
                                if (File.Exists(manifestPath)) throw new InvalidOperationException($"{manifestPath} already exists; import into an empty folder.");
                if (File.Exists(composePath)) throw new InvalidOperationException($"{composePath} already exists; import into an empty folder.");
                Directory.CreateDirectory(target);
                File.WriteAllText(manifestPath, result.ManifestJson);
                File.WriteAllText(composePath, result.ComposeYaml);
                Console.WriteLine($"Wrote {manifestPath}");
                Console.WriteLine($"Wrote {composePath}");
                foreach (var note in result.Notes) Console.WriteLine($"Note: {note}");
                Console.WriteLine("Next: edit \"summary\" and the health path in automaton.json, run automaton validate, then automaton create and automaton deploy.");
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
            {
                Console.Error.WriteLine($"Error: {exception.Message}");
                Environment.ExitCode = 1;
            }
        }, file, name, output);
        return command;
    }
}
