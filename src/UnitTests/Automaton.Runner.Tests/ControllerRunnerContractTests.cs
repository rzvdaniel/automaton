using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class ControllerRunnerContractTests
{
    [Test]
    public async Task ControllerRunner_ImportsTargetBackendControllerAndReturnsJsonEnvelope()
    {
        var python = ResolvePython();
        if (python is null)
        {
            Assert.Ignore("Python is not installed in the test environment.");
            return;
        }

        var controllerRunner = LocateControllerRunner();
        if (controllerRunner is null)
        {
            Assert.Ignore("controller_runner.py is not available from the test output directory.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "automaton-controller-runner-tests", Guid.NewGuid().ToString("N"));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var backendDirectory = Path.Combine(root, appId.ToString(), "targets", targetId.ToString(), "runtime", "backend");
        Directory.CreateDirectory(backendDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(backendDirectory, "todos.py"),
            "def handle(action_name, inputs):\n    print(f'handled:{action_name}')\n    return {'success': True, 'data': {'value': inputs.get('value')}, 'error': ''}\n");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = python,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = backendDirectory,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(controllerRunner);
            process.StartInfo.ArgumentList.Add(root);
            process.StartInfo.ArgumentList.Add(appId.ToString());
            process.StartInfo.ArgumentList.Add(targetId.ToString());
            process.StartInfo.ArgumentList.Add("todos");

            Assert.That(process.Start(), Is.True);
            await process.StandardInput.WriteAsync("{\"action\":\"list\",\"inputs\":{\"value\":7}}");
            process.StandardInput.Close();
            await process.WaitForExitAsync();

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            Assert.That(process.ExitCode, Is.EqualTo(0), stderr);

            using var response = JsonDocument.Parse(stdout);
            var rootElement = response.RootElement;
            Assert.That(rootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(rootElement.GetProperty("data").GetProperty("value").GetInt32(), Is.EqualTo(7));
            Assert.That(rootElement.GetProperty("output").GetString(), Does.Contain("handled:list"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string? ResolvePython()
    {
        foreach (var command in OperatingSystem.IsWindows() ? new[] { "python.exe", "python" } : new[] { "python3", "python" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    ArgumentList = { "--version" },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (process is null)
                    continue;
                process.WaitForExit();
                if (process.ExitCode == 0)
                    return command;
            }
            catch (Win32Exception)
            {
                // Try the next conventional Python command.
            }
        }

        return null;
    }

    private static string? LocateControllerRunner()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Automaton.Runner", "fastapi_server", "controller_runner.py");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}
