using Automaton.Core.Scripting.Engines.Python;

namespace Automaton.Runner.Console.Services;

public static class PythonEnvironmentDiagnostics
{
    public static PythonEnvironmentInfo DetectAndPrintIfVerbose(bool verbose)
    {
        var detected = Detect();
        if (verbose && detected != null)
        {
            PrintIndented(detected);
        }
        return detected;
    }

    public static PythonEnvironmentInfo Detect()
    {
        var detector = new PythonEnvironmentDetector();
        return detector.Detect();
    }

    public static void PrintIndented(PythonEnvironmentInfo detected)
    {
        if (detected == null) return;
        System.Console.WriteLine("🤖 Python Environment:");
        System.Console.WriteLine($" • Python Home: {detected.PythonHome}");
        System.Console.WriteLine($" • Python Executable: {detected.PythonExe}");
        System.Console.WriteLine($" • Python Version: {detected.PythonVersion}");
        System.Console.WriteLine($" • Python DLL Path: {detected.PythonDllPath}");
        System.Console.WriteLine($" • Architecture: {detected.Architecture}");
        System.Console.WriteLine($" • Distributions: {string.Join(", ", detected.Distributions)}");
        System.Console.WriteLine($" • Site-Packages: {string.Join(", ", detected.SitePackagesPaths)}");
        System.Console.WriteLine($" • Python Paths: {detected.PythonPaths}");
        if (detected.EnvironmentVariables != null && detected.EnvironmentVariables.Count >0)
        {
            System.Console.WriteLine($" • Environment Variables:");
            foreach (var kv in detected.EnvironmentVariables)
                System.Console.WriteLine($" - {kv.Key} = {kv.Value}");
        }
        System.Console.WriteLine($" • Auto-Detection Successful: {detected.IsAutoDetectionSuccessful}");
        if (!string.IsNullOrWhiteSpace(detected.Notes))
            System.Console.WriteLine($" • Notes: {detected.Notes}");
        System.Console.WriteLine();
    }
}
