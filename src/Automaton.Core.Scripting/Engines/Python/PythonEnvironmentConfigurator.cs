using Python.Runtime;

namespace Automaton.Core.Scripting.Engines.Python;

public class PythonEnvironmentConfigurator
{
    public void Configure(PythonEnvironmentInfo detected, ScriptEngineConfiguration configuration)
    {
        if (configuration.AutoDetectPythonPaths && detected != null)
            TryResolveAndSetPythonDll(detected);
    }

    private static void TryResolveAndSetPythonDll(PythonEnvironmentInfo detected)
    {
        if (!OperatingSystem.IsWindows() || !string.IsNullOrWhiteSpace(Runtime.PythonDLL))
            return;

        var candidateDirs = GetCandidateDirectories(detected);
        var bestMatch = FindBestPythonDll(candidateDirs, detected);
        if (!string.IsNullOrWhiteSpace(bestMatch) && File.Exists(bestMatch))
            Runtime.PythonDLL = bestMatch;
    }

    private static List<string> GetCandidateDirectories(PythonEnvironmentInfo detected)
    {
        var candidateDirs = new List<string>();
        AddEnvironmentHomeDirectories(candidateDirs, detected.PythonHome);
        AddExecutableDirectory(candidateDirs, detected.PythonExe);
        AddEnvironmentHomeDirectories(candidateDirs, Environment.GetEnvironmentVariable("PYTHONHOME"));
        AddPathDirectories(candidateDirs);

        return candidateDirs
            .Select(path => path.Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddEnvironmentHomeDirectories(List<string> directories, string? home)
    {
        if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
            return;

        directories.Add(home);
        AddExistingSubdirectory(directories, home, "DLLs");
        AddExistingSubdirectory(directories, home, "bin");
    }

    private static void AddExistingSubdirectory(List<string> directories, string home, string name)
    {
        var path = Path.Combine(home, name);
        if (Directory.Exists(path))
            directories.Add(path);
    }

    private static void AddExecutableDirectory(List<string> directories, string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return;

        var directory = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(directory))
            directories.Add(directory);
    }

    private static void AddPathDirectories(List<string> directories)
    {
        var pathEnvironment = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var path in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Directory.Exists(path))
                directories.Add(path);
        }
    }

    private static string? FindBestPythonDll(IEnumerable<string> directories, PythonEnvironmentInfo detected)
    {
        string? bestMatch = null;
        var bestScore = int.MinValue;
        var versionToken = detected.PythonMajor.HasValue && detected.PythonMinor.HasValue
            ? $"{detected.PythonMajor}{detected.PythonMinor}"
            : null;

        foreach (var directory in directories)
        {
            foreach (var file in FindPythonDllFiles(directory))
            {
                var score = ScorePythonDll(file, directory, detected, versionToken);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestMatch = file;
            }
        }

        return bestMatch;
    }

    private static IEnumerable<string> FindPythonDllFiles(string directory)
    {
        var files = TryEnumerateFiles(directory, "python3*.dll");
        return files.Count > 0 ? files : TryEnumerateFiles(directory, "python*.dll");
    }

    private static List<string> TryEnumerateFiles(string directory, string pattern)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern))
                files.Add(file);
        }
        catch
        {
            // A directory may become inaccessible during discovery; keep any matches already found.
        }

        return files;
    }

    private static int ScorePythonDll(
        string file,
        string directory,
        PythonEnvironmentInfo detected,
        string? versionToken)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileName(file),
            @"python(?<maj>\d)(?<min>\d{1,2})\.dll",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var score = GetVersionScore(match, detected, versionToken);

        if (!string.IsNullOrWhiteSpace(detected.PythonHome)
            && directory.StartsWith(detected.PythonHome, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        return score;
    }

    private static int GetVersionScore(
        System.Text.RegularExpressions.Match match,
        PythonEnvironmentInfo detected,
        string? versionToken)
    {
        if (!match.Success)
            return 10;

        var token = match.Groups["maj"].Value + match.Groups["min"].Value;
        if (string.IsNullOrWhiteSpace(versionToken))
            return 0;
        if (string.Equals(token, versionToken, StringComparison.Ordinal))
            return 1000;

        return token.StartsWith(detected.PythonMajor?.ToString() ?? string.Empty)
            ? 500
            : 0;
    }
}
