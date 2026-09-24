using Automaton.Cli.Models;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

internal static class RunnerInstallCommandHandler
{
    internal static async Task HandleAsync(
        ConfigService config,
        RunnerInstallService runnerService,
        string? name,
        string? server,
        string? key,
        bool native,
        bool replace,
        bool? autoStart,
        string? image,
        bool asJson)
    {
        try
        {
            var request = BuildRequest(
                config, runnerService, name, server, key, native, replace, autoStart, image, asJson);
            var result = await runnerService.InstallAsync(request);
            RunnerCommand.WriteResult(result, asJson);
            Environment.ExitCode = result.Success ? 0 : 1;
        }
        catch (Exception exception)
        {
            WriteFailure(exception, asJson);
        }
    }

    private static RunnerInstallRequest BuildRequest(
        ConfigService config,
        RunnerInstallService runnerService,
        string? name,
        string? server,
        string? key,
        bool native,
        bool replace,
        bool? autoStart,
        string? image,
        bool asJson)
    {
        var (_, storedApiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
        var runnerName = ResolveRunnerName(name, asJson);
        var resolvedServer = runnerService.GetDefaultServer(server);
        var resolvedKey = ResolveApiKey(key, storedApiKey, asJson);
        return new RunnerInstallRequest
        {
            RunnerName = runnerName,
            ApiServerUrl = resolvedServer,
            ApiKey = resolvedKey,
            Mode = native ? RunnerInstallModes.Native : RunnerInstallModes.Docker,
            AutoStart = ResolveAutoStart(autoStart, asJson, native),
            DockerImage = image,
            ReplaceExisting = replace,
            ProgressReporter = asJson ? null : message => Console.WriteLine(message)
        };
    }

    private static string ResolveRunnerName(string? name, bool asJson)
    {
        var resolvedName = string.IsNullOrWhiteSpace(name) && !asJson
            ? PromptForValue("Enter runner name")
            : name?.Trim();
        return !string.IsNullOrWhiteSpace(resolvedName)
            ? resolvedName
            : throw new InvalidOperationException("Runner name is required.");
    }

    private static string ResolveApiKey(string? key, string? storedApiKey, bool asJson)
    {
        var resolvedKey = string.IsNullOrWhiteSpace(key) ? storedApiKey : key.Trim();
        if (!string.IsNullOrWhiteSpace(resolvedKey)) return resolvedKey;
        return asJson
            ? throw new InvalidOperationException("Runner API key is required. Run automaton login first or pass --key.")
            : ReadSecretFromConsole("Enter runner API key");
    }

    private static bool ResolveAutoStart(bool? autoStart, bool asJson, bool native)
    {
        if (autoStart.HasValue) return autoStart.Value;
        if (asJson) return true;
        var prompt = native
            ? "Start automatically on machine boot?"
            : "Start automatically when Docker restarts?";
        return PromptForYesNo(prompt, true);
    }

    private static void WriteFailure(Exception exception, bool asJson)
    {
        var result = new RunnerCommandResult
        {
            Success = false,
            Summary = "Runner install failed",
            Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, false, exception.Message)],
            NextSteps = ["Run automaton login if server URL or API key has not been configured yet"]
        };
        RunnerCommand.WriteResult(result, asJson);
        Environment.ExitCode = 1;
    }

    private static string PromptForValue(string label)
    {
        Console.Write($"{label}: ");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    private static bool PromptForYesNo(string label, bool defaultValue)
    {
        Console.Write($"{label} [{(defaultValue ? "Y/n" : "y/N")}]: ");
        var value = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSecretFromConsole(string label)
    {
        Console.Write($"{label}: ");
        var buffer = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                RemoveLastCharacter(buffer);
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer.Add(key.KeyChar);
                Console.Write("*");
            }
        }
        return new string(buffer.ToArray()).Trim();
    }

    private static void RemoveLastCharacter(ICollection<char> buffer)
    {
        if (buffer.Count == 0) return;
        var list = (List<char>)buffer;
        list.RemoveAt(list.Count - 1);
        Console.Write("\b \b");
    }
}
