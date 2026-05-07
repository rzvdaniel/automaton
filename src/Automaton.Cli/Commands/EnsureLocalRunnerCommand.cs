using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class EnsureLocalRunnerCommand
{
    private const string DefaultImage = "rzvdaniel/automaton-runner:latest";
    private const string DefaultContainerName = "automaton-local-runner";

    public static Command Build(ConfigService config)
    {
        var nameOption = new Option<string>("--name", () => "Automaton Local Runner", "Runner name to register with the server");
        var imageOption = new Option<string>("--image", () => DefaultImage, "Runner Docker image to use");
        var containerOption = new Option<string>("--container-name", () => DefaultContainerName, "Docker container name");
        var assignOption = new Option<bool>("--assign", () => true, "Assign the ensured runner to the current linked app when possible");
        var forceOption = new Option<bool>("--force", "Remove any existing container with the same name before starting a new runner");
        var waitSecondsOption = new Option<int>("--wait-seconds", () => 20, "How long to wait for the runner to appear online");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("ensure-local-runner", "Best-effort Docker bootstrap for a local runner")
        {
            nameOption,
            imageOption,
            containerOption,
            assignOption,
            forceOption,
            waitSecondsOption,
            jsonOption
        };

        cmd.SetHandler(async (string runnerName, string image, string containerName, bool assign, bool force, int waitSeconds, bool asJson) =>
        {
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.TryLoadProjectConfig(cwd);
                var (server, apiKey) = config.ResolveCredentials(appConfig, cwd);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
                checks.Add(credentialsCheck);

                if (!credentialsCheck.Success || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Ensure local runner failed", Checks = checks, NextSteps = nextSteps.ToList() };
                    if (asJson)
                    {
                        CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                    }
                    else
                    {
                        CliDiagnosticsWriter.Write(failure);
                    }
                    Environment.ExitCode = 1;
                    return;
                }

                using var client = new AutomatonApiClient(server!, apiKey!);

                var existing = await FindRunnerByNameAsync(client, runnerName);
                if (existing != null)
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, true, $"reusing existing runner {existing.Value.id}"));
                    if (assign && appConfig != null)
                    {
                        await client.UpdateAppRunnerAsync(appConfig.AppId, existing.Value.id);
                        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, $"runner assigned to {appConfig.AppId}"));
                    }
                    else if (assign)
                    {
                        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, "runner ready, but no linked app was available to assign", IsWarning: true));
                    }

                    var reused = new CommandDiagnosticResult { Success = true, Summary = "Local runner ready", Checks = checks, NextSteps = [] };

                    if (asJson)
                    {
                        CliJson.Write(new { success = true, summary = reused.Summary, checks = reused.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = reused.NextSteps, reusedExisting = true, runnerId = existing.Value.id, runnerName = existing.Value.name, assigned = assign && appConfig != null });
                        return;
                    }

                    AnsiConsole.MarkupLine($"[green]✓[/] Reusing existing runner [bold]{existing.Value.name}[/] ({existing.Value.id})");
                    return;
                }

                await EnsureDockerAvailableAsync();
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Docker, true, "docker is available"));

                var apiKeyResult = await client.CreateApiKeyAsync($"{runnerName} runner key", "Created by automaton ensure-local-runner", ApiKeyScopes.Runner);
                var runnerApiKey = apiKeyResult.GetProperty("apiKey").GetString();

                if (string.IsNullOrWhiteSpace(runnerApiKey))
                {
                    throw new InvalidOperationException("Server did not return a runner API key.");
                }
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerApiKey, true, "runner API key created"));

                if (force)
                {
                    await RunDockerAsync(new[] { "rm", "-f", containerName }, ignoreFailure: true);
                }

                var runnerServerUrl = NormalizeServerUrlForDocker(server);
                var args = new List<string>
                {
                    "run",
                    "-d",
                    "--name", containerName,
                    "--restart", "unless-stopped",
                    "-e", $"AUTOMATON_SERVER_URL={runnerServerUrl}",
                    "-e", $"AUTOMATON_API_KEY={runnerApiKey}",
                    "-e", $"AUTOMATON_RUNNER_NAME={runnerName}"
                };

                if (RequiresDevelopmentCertificateBypass(runnerServerUrl))
                {
                    args.AddRange(new[] { "-e", "DOTNET_ENVIRONMENT=Development" });
                }

                args.Add(image);
                var containerId = await RunDockerAsync(args, ignoreFailure: false);
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, true, $"runner container started: {containerName}"));

                var ensured = await WaitForRunnerAsync(client, runnerName, waitSeconds);
                if (ensured == null)
                {
                    throw new InvalidOperationException("Runner container started, but the runner did not appear online within the wait window.");
                }
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerOnline, true, $"runner online: {ensured.Value.id}"));

                if (assign && appConfig != null)
                {
                    await client.UpdateAppRunnerAsync(appConfig.AppId, ensured.Value.id);
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, $"runner assigned to {appConfig.AppId}"));
                }
                else if (assign)
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, "runner ready, but no linked app was available to assign", IsWarning: true));
                }

                var ensuredResult = new CommandDiagnosticResult { Success = true, Summary = "Local runner ready", Checks = checks, NextSteps = [] };

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = true,
                        summary = ensuredResult.Summary,
                        checks = ensuredResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                        nextSteps = ensuredResult.NextSteps,
                        runnerId = ensured.Value.id,
                        runnerName = ensured.Value.name,
                        containerName,
                        containerId = containerId.Trim(),
                        assigned = assign && appConfig != null,
                        runnerServerUrl
                    });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Local runner [bold]{ensured.Value.name}[/] is online ({ensured.Value.id})");
                if (assign && appConfig != null)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Assigned to [cyan]{appConfig.Name}[/].");
                }
            }
            catch (Exception ex)
            {
                var checkName = checks.Any(check => check.Name == CommandDiagnosticCheckNames.Docker)
                    ? checks.Any(check => check.Name == CommandDiagnosticCheckNames.RunnerStart)
                        ? CommandDiagnosticCheckNames.RunnerOnline
                        : CommandDiagnosticCheckNames.Docker
                    : CommandDiagnosticCheckNames.Docker;

                checks.Add(new CommandDiagnosticCheck(checkName, false, ex.Message));
                nextSteps.Add("Make sure Docker is installed and running");
                nextSteps.Add("If the container started, inspect it with docker logs automaton-local-runner");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Ensure local runner failed", Checks = checks, NextSteps = nextSteps.ToList() };
                if (asJson)
                {
                    CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                }
                else
                {
                    CliDiagnosticsWriter.Write(failure);
                }
                Environment.ExitCode = 1;
            }
        }, nameOption, imageOption, containerOption, assignOption, forceOption, waitSecondsOption, jsonOption);

        return cmd;
    }

    private static async Task EnsureDockerAvailableAsync()
    {
        await RunDockerAsync(new[] { "version", "--format", "{{.Server.Version}}" }, ignoreFailure: false);
    }

    private static string NormalizeServerUrlForDocker(string server)
    {
        var uri = new Uri(server);
        if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return server;
        }

        var builder = new UriBuilder(uri)
        {
            Host = "host.docker.internal"
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static bool RequiresDevelopmentCertificateBypass(string server)
    {
        var uri = new Uri(server);
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string id, string name)?> FindRunnerByNameAsync(AutomatonApiClient client, string runnerName)
    {
        var runners = await client.ListRunnersAsync();
        if (runners.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var runner in runners.EnumerateArray())
        {
            var name = runner.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
            var id = runner.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
            var connected = runner.TryGetProperty("connectionId", out var connectionProperty)
                && connectionProperty.ValueKind != JsonValueKind.Null
                && !string.IsNullOrWhiteSpace(connectionProperty.GetString());

            if (connected && string.Equals(name, runnerName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(id))
            {
                return (id!, name ?? id!);
            }
        }

        return null;
    }

    private static async Task<(string id, string name)?> WaitForRunnerAsync(AutomatonApiClient client, string runnerName, int waitSeconds)
    {
        var attempts = Math.Max(1, waitSeconds / 2);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var match = await FindRunnerByNameAsync(client, runnerName);
            if (match != null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return null;
    }

    private static async Task<string> RunDockerAsync(IEnumerable<string> arguments, bool ignoreFailure)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start docker process.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !ignoreFailure)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? $"docker command failed with exit code {process.ExitCode}."
                : stderr.Trim());
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }
}