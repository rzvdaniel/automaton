using Python.Runtime;

namespace Automaton.Core.Scripting.Engines.Python;

public class PythonEnvironmentConfigurator
{
    public void Configure(PythonEnvironmentInfo detected, ScriptEngineConfiguration configuration)
    {
        if (configuration.AutoDetectPythonPaths && detected != null)
        {
            TryResolveAndSetPythonDll(detected, configuration);
        }
    }

    private void TryResolveAndSetPythonDll(PythonEnvironmentInfo detected, ScriptEngineConfiguration configuration)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!string.IsNullOrWhiteSpace(Runtime.PythonDLL)) return;

        var candidateDirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(detected.PythonHome) && Directory.Exists(detected.PythonHome))
        {
            candidateDirs.Add(detected.PythonHome);
            var dlls = Path.Combine(detected.PythonHome, "DLLs");
            var bin = Path.Combine(detected.PythonHome, "bin");
            if (Directory.Exists(dlls)) candidateDirs.Add(dlls);
            if (Directory.Exists(bin)) candidateDirs.Add(bin);
        }
        if (!string.IsNullOrWhiteSpace(detected.PythonExe))
        {
            var exeDir = Path.GetDirectoryName(detected.PythonExe);
            if (!string.IsNullOrWhiteSpace(exeDir)) candidateDirs.Add(exeDir);
        }
        var pyHomeEnv = Environment.GetEnvironmentVariable("PYTHONHOME");
        if (!string.IsNullOrWhiteSpace(pyHomeEnv) && Directory.Exists(pyHomeEnv))
        {
            candidateDirs.Add(pyHomeEnv);
            var dlls = Path.Combine(pyHomeEnv, "DLLs");
            var bin = Path.Combine(pyHomeEnv, "bin");
            if (Directory.Exists(dlls)) candidateDirs.Add(dlls);
            if (Directory.Exists(bin)) candidateDirs.Add(bin);
        }
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var p in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Directory.Exists(p)) candidateDirs.Add(p);
        }
        candidateDirs = candidateDirs
        .Select(p => p.Trim())
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
        string? bestMatch = null;
        int bestScore = int.MinValue;
        var versionToken = (detected.PythonMajor.HasValue && detected.PythonMinor.HasValue)
        ? ($"{detected.PythonMajor}{detected.PythonMinor}")
        : null;
        foreach (var dir in candidateDirs)
        {
            try
            {
                var files = Enumerable.Empty<string>();
                try { files = Directory.EnumerateFiles(dir, "python3*.dll"); } catch { }
                if (!files.Any())
                {
                    try { files = Directory.EnumerateFiles(dir, "python*.dll"); } catch { }
                }
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var m = System.Text.RegularExpressions.Regex.Match(name, @"python(?<maj>\d)(?<min>\d{1,2})\.dll", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    int score = 0;
                    if (m.Success)
                    {
                        var token = m.Groups["maj"].Value + m.Groups["min"].Value;
                        if (!string.IsNullOrWhiteSpace(versionToken))
                        {
                            if (string.Equals(token, versionToken, StringComparison.Ordinal))
                            {
                                score += 1000;
                            }
                            else if (token.StartsWith(detected.PythonMajor?.ToString() ?? string.Empty))
                            {
                                score += 500;
                            }
                        }
                    }
                    else
                    {
                        score += 10;
                    }
                    if (!string.IsNullOrWhiteSpace(detected.PythonHome) && dir.StartsWith(detected.PythonHome, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 50;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = file;
                    }
                }
            }
            catch { }
        }
        if (!string.IsNullOrWhiteSpace(bestMatch) && File.Exists(bestMatch))
        {
            Runtime.PythonDLL = bestMatch;
        }
    }
}
