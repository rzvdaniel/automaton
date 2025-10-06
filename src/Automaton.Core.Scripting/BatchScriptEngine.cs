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

    public ScriptExecutionResult ExecuteScript(string code, IDictionary<string, object> inputs)
    {
        // Delegate to Execute and wrap in ScriptExecutionResult
        var variables = Execute(code, inputs);

        var result = new ScriptExecutionResult
        {
            OutputVariables = variables
                .Where(kvp => kvp.Key != "error" && kvp.Key != "output")
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            StandardOutput = variables.ContainsKey("output") ? variables["output"]?.ToString() ?? string.Empty : string.Empty
        };

        // Check for errors
        if (variables.ContainsKey("error"))
        {
            result.Success = false;
            result.Error = new ScriptErrorInfo
            {
                ErrorType = "BatchScriptError",
                Message = variables["error"]?.ToString() ?? "Unknown error",
                StandardError = result.StandardOutput
            };
        }
        else
        {
            result.Success = true;
        }

        return result;
    }
}