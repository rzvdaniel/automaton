using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>automaton catalog: create and build an apps repository that an Automaton server can use as a catalog source.</summary>
public static class CatalogCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("catalog", "Create and build an apps repository for use as a catalog source")
        {
            BuildInit(config),
            BuildBuild()
        };
        return command;
    }

    private static Command BuildInit(ConfigService config)
    {
        var directory = new Argument<string>("directory", () => ".", "Folder to create the repository files in (empty or new)");
        var command = new Command("init", "Write the files of a new apps repository (example app, catalog list, CI workflow, README); nothing is pushed") { directory };
        command.SetHandler((string target) =>
        {
            try
            {
                var full = Path.GetFullPath(target);
                Directory.CreateDirectory(full);
                var written = CatalogInitService.Write(config, full);
                Console.WriteLine($"Created {written.Count} files in {full}:");
                foreach (var file in written) Console.WriteLine($"  {file}");
                Console.WriteLine("Next: read README.md, commit, push to GitHub, then add the repository under Apps → Catalog → Catalog sources in Studio.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Error: {exception.Message}");
                Environment.ExitCode = 1;
            }
        }, directory);
        return command;
    }

    private static Command BuildBuild()
    {
        var directory = new Argument<string>("directory", () => ".", "The apps repository folder (needs catalog-apps.json, and git history for the pinned commits)");
        var repo = new Option<string?>("--repo", "Repository as owner/name (default: \"repo\" in catalog-apps.json, then $GITHUB_REPOSITORY)");
        var output = new Option<string?>("--out", "Write the catalog here (default: catalog.json in the folder)");
        var validate = new Option<bool>("--validate", "Only check the entries; write nothing");
        var command = new Command("build", "Generate catalog.json from catalog-apps.json and each app's automaton.json") { directory, repo, output, validate };
        command.SetHandler((string target, string? repoName, string? outPath, bool validateOnly) =>
        {
            var full = Path.GetFullPath(target);
            var (json, problems) = CatalogBuildService.Build(full, repoName);
            if (json is null)
            {
                foreach (var problem in problems) Console.Error.WriteLine($"Error: {problem}");
                Environment.ExitCode = 1;
                return;
            }
            if (validateOnly) { Console.WriteLine("catalog-apps.json is valid."); return; }
            var path = outPath ?? Path.Combine(full, CatalogBuildService.CatalogFileName);
            File.WriteAllText(path, json);
            Console.WriteLine($"Wrote {path}");
        }, directory, repo, output, validate);
        return command;
    }
}
