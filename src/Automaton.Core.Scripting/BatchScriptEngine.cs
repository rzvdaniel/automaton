using Automaton.Core.Scripting;
using System.Diagnostics;

public class BatchScriptEngine : IScriptEngine, IOutputCallbackScriptEngine
{
    public Action<string>? OutputCallback { get; set; }

    public IDictionary<string, object> Execute(string code, IDictionary<string, object> inputs)
    {
        var tempFile = Path.GetTempFileName() + ".bat";
        File.WriteAllText(tempFile, code);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = tempFile,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // Add this line
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputLines = new List<string>();
        while (!process.StandardOutput.EndOfStream)
        {
            var line = process.StandardOutput.ReadLine();
            outputLines.Add(line);
            OutputCallback?.Invoke(line);
        }

        var errorLines = new List<string>();
        while (!process.StandardError.EndOfStream)
        {
            var line = process.StandardError.ReadLine();
            errorLines.Add(line);
        }

        process.WaitForExit();
        File.Delete(tempFile);

        var output = string.Join("\n", outputLines);
        var error = string.Join("\n", errorLines);

        var result = new Dictionary<string, object> { { "output", output } };
        if (!string.IsNullOrWhiteSpace(error))
        {
            var errorMessage = $"ERROR: {error}";
            result["error"] = errorMessage;
            OutputCallback?.Invoke(errorMessage);
        }

        return result;
    }
}