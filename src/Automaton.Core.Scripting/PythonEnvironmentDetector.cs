using System.Diagnostics;

namespace Automaton.Core.Scripting;

public class PythonEnvironmentDetector
{
    public PythonEnvironmentInfo Detect()
    {
        var info = new PythonEnvironmentInfo();
        var candidates = new[] { "python3", "python" };
        foreach (var exe in candidates)
        {
            try
            {
                var home = RunPythonAndReadStdOut(exe, "-c \"import sys; print(sys.exec_prefix)\"")?.Trim();
                if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
                    continue;

                var paths = RunPythonAndReadStdOut(exe, "-c \"import sys, os; print(os.pathsep.join(sys.path))\"")?.Trim();
                if (string.IsNullOrWhiteSpace(paths))
                    continue;

                var ver = RunPythonAndReadStdOut(exe, "-c \"import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')\"")?.Trim();
                var sysExe = RunPythonAndReadStdOut(exe, "-c \"import sys; print(sys.executable)\"")?.Trim();

                info.PythonHome = home;
                info.PythonPaths = paths;
                info.PythonExe = !string.IsNullOrWhiteSpace(sysExe) ? sysExe : null;

                if (!string.IsNullOrWhiteSpace(ver))
                {
                    var parts = ver.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[0], out var maj) && int.TryParse(parts[1], out var min))
                    {
                        info.PythonMajor = maj;
                        info.PythonMinor = min;
                    }
                }
                break;
            }
            catch
            {
                // try next
            }
        }
        return info;
    }

    private static string? RunPythonAndReadStdOut(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return null;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return output;
    }
}
