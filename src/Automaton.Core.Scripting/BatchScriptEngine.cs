using Automaton.Core.Scripting;
using System.Diagnostics;

public class BatchScriptEngine : IScriptEngine
{
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
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        File.Delete(tempFile);

        return new Dictionary<string, object> { { "output", output } };
    }
}