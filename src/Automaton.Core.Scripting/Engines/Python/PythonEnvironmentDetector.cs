using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Automaton.Core.Scripting.Engines.Python;

public class PythonEnvironmentDetector
{
    public PythonEnvironmentInfo Detect()
    {
        var info = new PythonEnvironmentInfo();
        foreach (var executable in new[] { "python3", "python" })
        {
            try
            {
                if (!TryDetectEnvironment(executable, info))
                    continue;

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

    private static bool TryDetectEnvironment(string executable, PythonEnvironmentInfo info)
    {
        var home = ReadPythonValue(executable, "import sys; print(sys.exec_prefix)");
        if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
            return false;

        var paths = ReadPythonValue(executable, "import sys, os; print(os.pathsep.join(sys.path))");
        if (string.IsNullOrWhiteSpace(paths))
            return false;

        var version = ReadPythonValue(executable, "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')");
        var sysExecutable = ReadPythonValue(executable, "import sys; print(sys.executable)");
        var dllPath = ReadPythonValue(executable, "import sys; import os; print(getattr(sys, 'dllhandle', None) or os.path.join(sys.exec_prefix, 'python' + str(sys.version_info.major) + str(sys.version_info.minor) + '.dll'))");
        var architecture = ReadPythonValue(executable, "import platform; print(platform.architecture()[0])");
        var distribution = ReadPythonValue(executable, "import platform; print(platform.python_implementation())");
        var sitePackages = ReadPythonValue(executable, "import site; print(site.getsitepackages())");

        SetEnvironmentDetails(info, home, paths, version, sysExecutable, dllPath, architecture);
        AddDistribution(info, distribution);
        AddSitePackages(info, sitePackages);
        SetPythonVersion(info, version);
        return true;
    }

    private static string? ReadPythonValue(string executable, string command) =>
        RunPythonAndReadStdOut(executable, $"-c \"{command}\"")?.Trim();

    private static void SetEnvironmentDetails(
        PythonEnvironmentInfo info,
        string home,
        string paths,
        string? version,
        string? executable,
        string? dllPath,
        string? architecture)
    {
        info.PythonHome = home;
        info.PythonPaths = paths;
        info.PythonExe = !string.IsNullOrWhiteSpace(executable) ? executable : null;
        info.PythonVersion = version;
        info.PythonDllPath = dllPath;
        info.Architecture = architecture;
        info.EnvironmentVariables["PYTHONHOME"] = home;
        info.EnvironmentVariables["PYTHONPATH"] = paths;
    }

    private static void AddDistribution(PythonEnvironmentInfo info, string? distribution)
    {
        if (!string.IsNullOrWhiteSpace(distribution))
            info.Distributions.Add(distribution);
    }

    private static void AddSitePackages(PythonEnvironmentInfo info, string? sitePackages)
    {
        if (string.IsNullOrWhiteSpace(sitePackages))
            return;

        var cleaned = sitePackages.Trim('[', ']', ' ', '\'');
        foreach (var entry in cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = entry.Trim(' ', '\'');
            if (!string.IsNullOrWhiteSpace(path))
                info.SitePackagesPaths.Add(path);
        }
    }

    private static void SetPythonVersion(PythonEnvironmentInfo info, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return;

        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor))
        {
            return;
        }

        info.PythonMajor = major;
        info.PythonMinor = minor;
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
