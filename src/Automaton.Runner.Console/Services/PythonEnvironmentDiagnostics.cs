using Automaton.Core.Scripting.Engines.Python;

namespace Automaton.Runner.Console.Services;

public static class PythonEnvironmentDiagnostics
{
    public static PythonEnvironmentInfo DetectAndPrintIfVerbose(bool verbose)
    {
        var detector = new PythonEnvironmentDetector();
        var detected = detector.Detect();
        if (verbose && detected != null)
        {
            System.Console.WriteLine("=== Python Environment Autodetection Details ===");
            System.Console.WriteLine($"PythonHome: {detected.PythonHome}");
            System.Console.WriteLine($"PythonPaths: {detected.PythonPaths}");
            System.Console.WriteLine($"PythonExe: {detected.PythonExe}");
            System.Console.WriteLine($"PythonVersion: {detected.PythonVersion}");
            System.Console.WriteLine($"PythonDllPath: {detected.PythonDllPath}");
            System.Console.WriteLine($"Architecture: {detected.Architecture}");
            System.Console.WriteLine($"Distributions: {string.Join(", ", detected.Distributions)}");
            System.Console.WriteLine($"SitePackagesPaths: {string.Join(", ", detected.SitePackagesPaths)}");
            System.Console.WriteLine($"EnvironmentVariables: {string.Join(", ", detected.EnvironmentVariables.Select(kv => kv.Key + "=" + kv.Value))}");
            System.Console.WriteLine($"IsAutoDetectionSuccessful: {detected.IsAutoDetectionSuccessful}");
            System.Console.WriteLine($"Notes: {detected.Notes}");
        }
        return detected;
    }
}
