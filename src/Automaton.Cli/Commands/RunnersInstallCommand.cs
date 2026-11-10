using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

public static class RunnersInstallCommand
{
    internal const int InstallerTimeoutSeconds = 900;

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
        string? runnerId = null;
        try
        {
            var name = parsed.GetValueForOption(nameOption)?.Trim() ?? "";
            var ssh = parsed.GetValueForOption(sshOption);
            var print = ValidateMode(name, ssh, parsed.GetValueForOption(localOption), parsed.GetValueForOption(printOption), parsed.GetValueForOption(timeoutOption));
            if (print && asJson) throw new ArgumentException("--json cannot be combined with --print-command.");
            ConfirmHostAccess(parsed.GetValueForOption(yesOption));
            var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
            using var client = new AutomatonApiClient(server, apiKey);
            var request = new InstallRequest(name, parsed.GetValueForOption(projectOption), ssh, print, parsed.GetValueForOption(timeoutOption), InstallerTimeoutSeconds);
            var id = await InstallAsync(client, request, ExecuteAsync, TimeSpan.FromSeconds(3), context.GetCancellationToken(), found => runnerId = found);
            if (asJson) CliJson.Write(new { runnerId = id, name, status = "connected" });
            else Console.Out.WriteLine($"Runner {name} connected ({id}).");
            context.ExitCode = 0;
        }
        catch (Exception error)
        {
            if (asJson) CliJson.Write(new { success = false, error = error.Message, runnerId });
            else Console.Error.WriteLine($"Runner installation failed: {error.Message}");
            context.ExitCode = 1;
        }
    }

    internal sealed record InstallRequest(string Name, string? Project, string? Ssh, bool Print, int TimeoutSeconds, int InstallerTimeoutSeconds);

    /// <summary>Resolves the project first, then installs, waits, and attaches the Runner. Returns the runner id.</summary>
    internal static async Task<string> InstallAsync(AutomatonApiClient client, InstallRequest request,
        Func<string, string?, int, CancellationToken, Task> execute, TimeSpan pollDelay, CancellationToken ct,
        Action<string>? onRunnerId = null)
    {
        string? projectId = null;
        if (!string.IsNullOrWhiteSpace(request.Project))
            projectId = ProjectCommand.PickProject(await client.ListProjectsAsync(ct), request.Project).Id;
        var installation = await client.CreateRunnerInstallationAsync(request.Name, ct);
        var command = installation.GetProperty("installationCommand").GetString() ?? throw new InvalidOperationException("Installation command missing.");
        if (request.Print)
        {
            Console.Error.WriteLine($"One-time secret: keep this command private. It expires at {installation.GetProperty("expiresAtUtc").GetString()}.");
            Console.Out.WriteLine(command);
        }
        else await execute(command, request.Ssh, request.InstallerTimeoutSeconds, ct);
        var id = await WaitForRunnerAsync(client, installation.GetProperty("installationId").GetString()!, request.TimeoutSeconds, pollDelay, ct);
        onRunnerId?.Invoke(id);
        if (projectId is not null)
        {
            try { await client.AddRunnerToProjectAsync(projectId, id, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Runner {id} connected but adding it to the project failed: {error.Message}", error);
            }
        }
        return id;
    }

    internal static Func<bool> IsNonInteractive { get; set; } = () => Console.IsInputRedirected || Console.IsOutputRedirected;
    internal static Func<bool> Prompt { get; set; } = () =>
        AnsiConsole.Confirm("I understand the Runner can manage Docker containers on this machine. Continue?", false);

    internal static bool ValidateMode(string name, string? ssh, bool local, bool print, int timeout)
    {
        if (name.Length is < 1 or > 120) throw new ArgumentException("Runner name must be 1 to 120 characters.");
        var modes = (ssh is not null ? 1 : 0) + (local ? 1 : 0) + (print ? 1 : 0);
        if (modes > 1) throw new ArgumentException("Choose one of --ssh, --local, or --print-command.");
        if (modes == 0)
        {
            if (IsNonInteractive())
                throw new ArgumentException("Choose --ssh, --local, or --print-command in a non-interactive terminal.");
            print = true;
        }
        if (ssh is not null && (ssh.Length == 0 || ssh.StartsWith('-') || ssh.Any(char.IsWhiteSpace)))
            throw new ArgumentException("--ssh requires a user@host without whitespace or options.");
        if (timeout < 1) throw new ArgumentException("--timeout must be positive.");
        return print;
    }

    internal static void ConfirmHostAccess(bool yes)
    {
        Console.Error.WriteLine("Machine Management gives this trusted Runner access to the host Docker socket. A compromise of the Runner could control host Docker. Keep app and Action code in isolated containers.");
        if (yes) return;
        if (IsNonInteractive()) throw new InvalidOperationException("Use --yes to acknowledge host access in non-interactive mode.");
        if (!Prompt()) throw new InvalidOperationException("Installation cancelled.");
    }

    // The script runs in a POSIX shell: any CR (Windows Environment.NewLine, CRLF from the server) would break the here-document.
    internal static string ToShellInput(string command) => command.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n') + "\n";

    internal static async Task ExecuteAsync(string command, string? ssh, int timeoutSeconds, CancellationToken ct)
    {
        var start = new ProcessStartInfo(ssh is null ? "sudo" : "ssh")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        if (ssh is null) { start.ArgumentList.Add("bash"); start.ArgumentList.Add("-s"); }
        else { start.ArgumentList.Add("-T"); start.ArgumentList.Add("--"); start.ArgumentList.Add(ssh); start.ArgumentList.Add("sudo bash -s"); }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start installer process.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
            var stderr = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await process.StandardInput.WriteAsync(ToShellInput(command).AsMemory(), limit.Token);
                process.StandardInput.Close();
            }
            catch (IOException) { /* installer exited early; its output is reported below */ }
            await process.WaitForExitAsync(limit.Token);
            var output = (await stdout + "\n" + await stderr).Trim();
            if (process.ExitCode != 0)
            {
                var token = Regex.Match(command, "AUTOMATON_ENROLLMENT_TOKEN=['\"]?([^'\" ]+)").Groups[1].Value;
                if (token.Length > 0) output = output.Replace(token, "[redacted]", StringComparison.Ordinal);
                var lastLines = string.Join('\n', output.Split('\n').TakeLast(8));
                var needsSudoHint = ssh is null && (output.Contains("sudo:", StringComparison.Ordinal)
                    || output.Contains("a terminal is required", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("password is required", StringComparison.OrdinalIgnoreCase));
                var localHint = needsSudoHint
                    ? "\n--local installs on this machine (it needs sudo here). For a remote server, use --ssh user@host."
                    : string.Empty;
                throw new InvalidOperationException($"Installer exited {process.ExitCode}: {lastLines}{localHint}");
            }
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"Installer did not finish within {timeoutSeconds} seconds.");
        }
    }

    internal static async Task<string> WaitForRunnerAsync(AutomatonApiClient client, string installationId, int timeout, TimeSpan pollDelay, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(timeout));
        var failures = 0;
        try
        {
            while (true)
            {
                JsonElement response;
                try
                {
                    response = await client.GetRunnerInstallationStatusAsync(installationId, limit.Token);
                    failures = 0;
                }
                catch (HttpRequestException error) when (++failures <= 5 && !Regex.IsMatch(error.Message, @"API error 4\d\d"))
                {
                    Console.Error.WriteLine($"Waiting for Runner: transient error ({error.Message}), retrying");
                    await Task.Delay(pollDelay * failures, limit.Token);
                    continue;
                }
                var status = response.GetProperty("status").GetString();
                if (status == "RunnerConnected") return response.GetProperty("runnerId").GetString()!;
                if (status == "Expired") throw new InvalidOperationException("Installation token expired before the Runner connected.");
                Console.Error.WriteLine($"Waiting for Runner: {status}");
                await Task.Delay(pollDelay, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Runner did not connect within {timeout} seconds.");
        }
    }
}
