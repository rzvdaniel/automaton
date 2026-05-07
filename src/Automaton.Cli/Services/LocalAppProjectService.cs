using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public class LocalAppProjectService(ConfigService configService)
{
    private static readonly string[] RootMarkers = ["automaton.json", "index.html", "frontend", "dist", "backend"];

    public LocalAppContext ResolveContext(string? startDirectory = null)
    {
        var rootPath = FindRoot(startDirectory ?? Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException("Could not find an Automaton app root. Run the command from an app folder or one of its subdirectories.");

        var config = configService.TryLoadProjectConfig(rootPath);
        var frontendPath = ResolveFrontendPath(rootPath, config);
        var indexHtmlPath = Path.Combine(frontendPath, "index.html");

        return new LocalAppContext
        {
            RootPath = rootPath,
            FrontendPath = frontendPath,
            IndexHtmlPath = indexHtmlPath,
            BackendPath = Path.Combine(rootPath, "backend"),
            MocksPath = Path.Combine(rootPath, "backend", "mocks"),
            DataPath = Path.Combine(rootPath, "data"),
            DisplayName = config?.Name?.Trim() switch
            {
                { Length: > 0 } configuredName => configuredName,
                _ => new DirectoryInfo(rootPath).Name
            },
            Config = config
        };
    }

    public string? FindRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        string? match = null;

        while (current is not null)
        {
            if (LooksLikeAppRoot(current.FullName))
            {
                match = current.FullName;
            }

            current = current.Parent;
        }

        return match;
    }

    public string ResolveFrontendPath(string rootPath, AutomatonConfig? config)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(config?.DeployDir))
        {
            candidates.Add(Path.Combine(rootPath, config.DeployDir));
        }

        candidates.Add(rootPath);
        candidates.Add(Path.Combine(rootPath, "frontend"));
        candidates.Add(Path.Combine(rootPath, "dist"));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }
        }

        return candidates.First(path => Directory.Exists(path) || path == rootPath);
    }

    private static bool LooksLikeAppRoot(string path)
    {
        return RootMarkers.Any(marker => File.Exists(Path.Combine(path, marker)) || Directory.Exists(Path.Combine(path, marker)));
    }
}
