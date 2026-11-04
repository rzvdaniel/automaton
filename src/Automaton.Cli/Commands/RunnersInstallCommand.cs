using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

public static class RunnersInstallCommand
{
    public static Command Build(ConfigService config)
    {
        var name = new Option<string>("--name", "Name of the new Runner") { IsRequired = true };
        var project = new Option<string?>("--project", "Project ID or exact name to add the Runner to");
        var ssh = new Option<string?>("--ssh", "Install over SSH as user@host");
        var local = new Option<bool>("--local", "Install on this machine");
        var print = new Option<bool>("--print-command", "Print the one-time installation command");
        var yes = new Option<bool>("--yes", "Acknowledge Machine Management host access without prompting");
        var timeout = new Option<int>("--timeout", () => 300, "Seconds to wait for the Runner to connect");
        var json = new Option<bool>("--json", "Print the connected Runner as JSON");
        var command = new Command("install", "Install a Machine Management Runner") { name, project, ssh, local, print, yes, timeout, json };
        command.SetHandler((InvocationContext context) => RunAsync(config, context, name, project, ssh, local, print, yes, timeout, json));
        return command;
    }

    private static async Task RunAsync(ConfigService config, InvocationContext context,
        Option<string> nameOption, Option<string?> projectOption, Option<string?> sshOption,
        Option<bool> localOption, Option<bool> printOption, Option<bool> yesOption,
        Option<int> timeoutOption, Option<bool> jsonOption)
    {
        var parsed = context.ParseResult;
        var asJson = parsed.GetValueForOption(jsonOption);
        try
        {
            var name = parsed.GetValueForOption(nameOption)?.Trim() ?? "";
            var ssh = parsed.GetValueForOption(sshOption);
            var print = ValidateMode(name, ssh, parsed.GetValueForOption(localOption), parsed.GetValueForOption(printOption), parsed.GetValueForOption(timeoutOption));
            if (print && asJson) throw new ArgumentException("--json cannot be combined with --print-command.");
            ConfirmHostAccess(parsed.GetValueForOption(yesOption));
            var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
            using var client = new AutomatonApiClient(server, apiKey);
            var installation = await client.CreateRunnerInstallationAsync(name, context.GetCancellationToken());
            var command = installation.GetProperty("installationCommand").GetString() ?? throw new InvalidOperationException("Installation command missing.");
            if (print)
            {
                Console.Error.WriteLine($"One-time secret: keep this command private. It expires at {installation.GetProperty("expiresAtUtc").GetString()}.");
                Console.Out.WriteLine(command);
            }
            else await ExecuteAsync(command, ssh, context.GetCancellationToken());
            var id = await WaitForRunnerAsync(client, installation.GetProperty("installationId").GetString()!, parsed.GetValueForOption(timeoutOption), context.GetCancellationToken());
            var project = parsed.GetValueForOption(projectOption);
            if (!string.IsNullOrWhiteSpace(project))
            {
                var (projectId, _) = ProjectCommand.PickProject(await client.ListProjectsAsync(context.GetCancellationToken()), project);
                await client.AddRunnerToProjectAsync(projectId, id, context.GetCancellationToken());
            }
            if (asJson) CliJson.Write(new { runnerId = id, name, status = "connected" });
            else Console.Out.WriteLine($"Runner {name} connected ({id}).");
            context.ExitCode = 0;
        }
        catch (Exception error)
        {
            if (asJson) CliJson.Write(new { success = false, error = error.Message });
            else Console.Error.WriteLine($"Runner installation failed: {error.Message}");
            context.ExitCode = 1;
        }
    }

    private static bool ValidateMode(string name, string? ssh, bool local, bool print, int timeout)
    {
        if (name.Length is < 1 or > 120) throw new ArgumentException("Runner name must be 1 to 120 characters.");
        var modes = (ssh is not null ? 1 : 0) + (local ? 1 : 0) + (print ? 1 : 0);
        if (modes > 1) throw new ArgumentException("Choose one of --ssh, --local, or --print-command.");
        if (modes == 0)
        {
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new ArgumentException("Choose --ssh, --local, or --print-command in a non-interactive terminal.");
            print = true;
        }
        if (ssh is not null && (ssh.Length == 0 || ssh.StartsWith('-') || ssh.Any(char.IsWhiteSpace)))
            throw new ArgumentException("--ssh requires a user@host without whitespace or options.");
        if (timeout < 1) throw new ArgumentException("--timeout must be positive.");
        return print;
    }

    private static void ConfirmHostAccess(bool yes)
    {
        Console.Error.WriteLine("Machine Management gives this trusted Runner access to the host Docker socket. A compromise of the Runner could control host Docker. Keep app and Action code in isolated containers.");
        if (yes) return;
        if (Console.IsInputRedirected) throw new InvalidOperationException("Use --yes to acknowledge host access in non-interactive mode.");
        if (!AnsiConsole.Confirm("I understand the Runner can manage Docker containers on this machine. Continue?", false))
            throw new InvalidOperationException("Installation cancelled.");
    }

    internal static async Task ExecuteAsync(string command, string? ssh, CancellationToken ct)
    {
        var start = new ProcessStartInfo(ssh is null ? "sudo" : "ssh")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        if (ssh is null) { start.ArgumentList.Add("bash"); start.ArgumentList.Add("-s"); }
        else { start.ArgumentList.Add("-T"); start.ArgumentList.Add(ssh); start.ArgumentList.Add("sudo bash -s"); }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start installer process.");
        try
        {
            await process.StandardInput.WriteLineAsync(command.AsMemory(), ct);
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var output = (await stdout + "\n" + await stderr).Trim();
            if (process.ExitCode != 0)
            {
                var token = Regex.Match(command, "AUTOMATON_ENROLLMENT_TOKEN=['\"]?([^'\" ]+)").Groups[1].Value;
                if (token.Length > 0) output = output.Replace(token, "[redacted]", StringComparison.Ordinal);
                var lastLines = string.Join('\n', output.Split('\n').TakeLast(8));
                throw new InvalidOperationException($"Installer exited {process.ExitCode}: {lastLines}");
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<string> WaitForRunnerAsync(AutomatonApiClient client, string installationId, int timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(timeout));
        try
        {
            while (true)
            {
                var response = await client.GetRunnerInstallationStatusAsync(installationId, limit.Token);
                var status = response.GetProperty("status").GetString();
                if (status == "RunnerConnected") return response.GetProperty("runnerId").GetString()!;
                if (status == "Expired") throw new InvalidOperationException("Installation token expired before the Runner connected.");
                Console.Error.WriteLine($"Waiting for Runner: {status}");
                await Task.Delay(TimeSpan.FromSeconds(3), limit.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Runner did not connect within {timeout} seconds.");
        }
    }
}
