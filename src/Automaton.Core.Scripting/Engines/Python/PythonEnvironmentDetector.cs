using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Automaton.Core.Scripting.Engines.Python;

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

                var ver = RunPythonAndReadStdOut(exe, "-c \"import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')\"")?.Trim();
                var sysExe = RunPythonAndReadStdOut(exe, "-c \"import sys; print(sys.executable)\"")?.Trim();
                var dllPath = RunPythonAndReadStdOut(exe, "-c \"import sys; import os; print(getattr(sys, 'dllhandle', None) or os.path.join(sys.exec_prefix, 'python' + str(sys.version_info.major) + str(sys.version_info.minor) + '.dll'))\"")?.Trim();
                var arch = RunPythonAndReadStdOut(exe, "-c \"import platform; print(platform.architecture()[0])\"")?.Trim();
                var dist = RunPythonAndReadStdOut(exe, "-c \"import platform; print(platform.python_implementation())\"")?.Trim();
                var sitePackages = RunPythonAndReadStdOut(exe, "-c \"import site; print(site.getsitepackages())\"")?.Trim();

                info.PythonHome = home;
                info.PythonPaths = paths;
                info.PythonExe = !string.IsNullOrWhiteSpace(sysExe) ? sysExe : null;
                info.PythonVersion = ver;
                info.PythonDllPath = dllPath;
                info.Architecture = arch;
                if (!string.IsNullOrWhiteSpace(dist))
                    info.Distributions.Add(dist);
                if (!string.IsNullOrWhiteSpace(sitePackages))
                {
                    // Try to parse as list
                    var cleaned = sitePackages.Trim('[', ']', ' ', '\'');
                    var split = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in split)
                    {
                        var p = s.Trim(' ', '\'');
                        if (!string.IsNullOrWhiteSpace(p))
                            info.SitePackagesPaths.Add(p);
                    }
                }
                // Environment variables
                info.EnvironmentVariables["PYTHONHOME"] = home;
                info.EnvironmentVariables["PYTHONPATH"] = paths;
                // Version breakdown
                if (!string.IsNullOrWhiteSpace(ver))
                {
                    var parts = ver.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[0], out var maj) && int.TryParse(parts[1], out var min))
                    {
                        info.PythonMajor = maj;
                        info.PythonMinor = min;
                    }
                }
                info.IsAutoDetectionSuccessful = true;
                break;
            }
            catch (Exception ex)
            {
                info.Notes = ex.Message;
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
