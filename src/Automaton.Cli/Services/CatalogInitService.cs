using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

/// <summary>Writes the files of a new apps repository: one example app, the catalog list, the CI workflow and a README. It never calls git or GitHub.</summary>
internal static class CatalogInitService
{
    internal const string MinimumServerVersion = "0.2.32";
    private const string ExampleFolder = "apps/hello";

    /// <summary>Returns the files written, relative to <paramref name="directory"/>.</summary>
    internal static IReadOnlyList<string> Write(ConfigService config, string directory)
    {
        var template = NewAppTemplate.Create("static");
        var files = new Dictionary<string, string>
        {
            ["README.md"] = Readme,
            [CatalogBuildService.ConfigFileName] = CatalogList,
            [".github/workflows/catalog.yml"] = Workflow
        };
        foreach (var (name, contents) in template.Files) files[$"{ExampleFolder}/{name}"] = contents + "\n";
        var manifest = $"{ExampleFolder}/automaton.json";
        var ignoreFile = $"{ExampleFolder}/.automatonignore";
        foreach (var path in files.Keys.Append(manifest).Append(ignoreFile))
            if (Path.Exists(Path.Combine(directory, path)))
                throw new IOException($"'{path}' already exists. Choose an empty directory.");

        foreach (var (path, contents) in files)
        {
            var full = Path.Combine(directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents);
        }
        config.SaveProjectConfig(Path.Combine(directory, ExampleFolder), new AutomatonConfig
        {
            Name = "Hello",
            Summary = "A tiny static page that shows your catalog works.",
            Application = template.Contract
        }, overwrite: false);
        return [.. files.Keys.Append(manifest).Append(ignoreFile).Order()];
    }

    private static string CatalogList => $$"""
        {
          "apps": [
            { "folder": "{{ExampleFolder}}", "category": "Examples", "minServerVersion": "{{MinimumServerVersion}}" }
          ]
        }

        """;

    private const string Readme = """
        # My Automaton apps

        An apps repository for [Automaton](https://automaton.monster): each folder under `apps/` is an app with its own
        `automaton.json`, and `catalog.json` is the index a server reads to list them under **Apps → Catalog**.

        ## Publish it

        1. Push this repository to GitHub (`git init`, `git add .`, `git commit`, `git push`).
        2. The `Catalog` workflow runs on every push to `main`: it builds `catalog.json` with `automaton catalog build`
           and commits it to `main` (it needs an Automaton CLI that has `automaton catalog`, so a CLI published from
           Automaton Server 0.2.36 on). If `main` requires pull requests, CI cannot push there: see the apps repository
           format in the Automaton docs for a variant that opens and merges a pull request.
        3. As a system administrator, open **Apps → Catalog → Catalog sources** in Studio and add
           `https://github.com/<you>/<this repository>`. A private repository needs a fine-grained read-only token
           (Contents access to this one repository).

        ## Add an app

        Create a folder with the app and its `automaton.json` (`name` and `summary` are required for the catalog), add it to
        `catalog-apps.json` with a category and the oldest Automaton Server version that can run it, and run
        `automaton validate` inside the app folder (its `applicationContract` check must pass; the link checks apply once you deploy the app). `automaton catalog build --validate` checks the list.

        Each catalog entry is pinned to the last commit that changed its folder. Commit your app before building.
        """;

    private const string Workflow = """
        name: Catalog

        # catalog.json (in main) is the index an Automaton server reads. Pull requests check that every entry is valid;
        # a push to main regenerates it and commits it if it changed.
        on:
          pull_request:
          push:
            branches: [main]

        permissions:
          contents: write

        concurrency:
          group: catalog-${{ github.ref }}
          cancel-in-progress: false

        jobs:
          catalog:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4
                with:
                  fetch-depth: 0
              - uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: 10.0.x
              - run: dotnet tool install --global Automaton.Cli
              - name: Check every entry (pull request)
                if: github.event_name == 'pull_request'
                run: automaton catalog build --repo "$GITHUB_REPOSITORY" --validate
              - name: Regenerate catalog.json and commit it (main)
                if: github.event_name == 'push'
                run: |
                  automaton catalog build --repo "$GITHUB_REPOSITORY"
                  git add catalog.json
                  if git diff --cached --quiet; then echo "catalog.json is current."; exit 0; fi
                  git config user.name "github-actions[bot]"
                  git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
                  git commit -m "Update catalog.json"
                  git push
        """;
}
