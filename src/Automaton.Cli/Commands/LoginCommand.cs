using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LoginCommand
{
    public static Command Build(ConfigService config)
    {
        var serverOption = new Option<string?>("--server", $"Automaton server URL (defaults to {ConfigService.DefaultHostedServer})");
        var keyOption = new Option<string?>(["--key", "-k"], "Use an existing API key (prefer --key-from-env for automation)");
        keyOption.Arity = ArgumentArity.ZeroOrOne;
        var deviceOption = new Option<bool>("--device", "Do not open a browser; display the authorization URL and code");
        var keyFromEnvOption = new Option<bool>("--key-from-env", "Read the existing API key from AUTOMATON_API_KEY");
        var globalOption = new Option<bool>("--global", "Store credentials in ~/.automaton/config.json instead of the current project");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("login", "Authorize the Automaton CLI or validate an existing API key")
        {
            serverOption,
            keyOption,
            deviceOption,
            keyFromEnvOption,
            globalOption,
            jsonOption
        };

        cmd.SetHandler(async (string? server, string? key, bool device, bool keyFromEnv, bool global, bool asJson) =>
        {
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var existing = config.ResolveExistingConfig(global, cwd);
                server = string.IsNullOrWhiteSpace(server)
                    ? existing.Server ?? ConfigService.DefaultHostedServer
                    : server.TrimEnd('/');

                var completionService = new CliAuthorizationCompletionService(config);
                if (existing.PendingAuthorizationCompletion is not null
                    && !await completionService.TryCompletePendingAsync(cwd, global))
                {
                    checks.Add(new CommandDiagnosticCheck(
                        CommandDiagnosticCheckNames.Authentication,
                        true,
                        "credential is saved; server acknowledgement remains pending and will be retried automatically",
                        IsWarning: true));
                    if (!asJson)
                        Console.Error.WriteLine("Warning: CLI credential is saved, but server acknowledgement is pending. Automaton will retry automatically.");
                }

                if (keyFromEnv && device)
                    throw new InvalidOperationException("Choose either --device or --key-from-env, not both.");

                var keyWasRequested = Environment.GetCommandLineArgs()
                    .Any(argument => string.Equals(argument, "--key", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(argument, "-k", StringComparison.OrdinalIgnoreCase));
                if (keyWasRequested && string.IsNullOrWhiteSpace(key) && !keyFromEnv)
                    key = PromptForApiKey();

                if (keyFromEnv)
                {
                    key = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
                    if (string.IsNullOrWhiteSpace(key))
                        throw new InvalidOperationException("AUTOMATON_API_KEY is not set.");
                }

                if (!string.IsNullOrWhiteSpace(key))
                {
                    await LoginWithExistingKeyAsync(config, cwd, server, key, global, asJson, checks);
                    return;
                }

                await LoginWithDeviceAuthorizationAsync(config, cwd, server, global, device, asJson, checks);
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, false, ex.Message));
                nextSteps.Add("Run automaton login to start browser/device authorization");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Login failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, serverOption, keyOption, deviceOption, keyFromEnvOption, globalOption, jsonOption);

        return cmd;
    }

    private static async Task LoginWithDeviceAuthorizationAsync(
        ConfigService config,
        string cwd,
        string server,
        bool global,
        bool forceDevice,
        bool asJson,
        List<CommandDiagnosticCheck> checks)
    {
        using var client = new AutomatonApiClient(server);
        var authorization = await client.CreateCliAuthorizationRequestAsync(Environment.MachineName);

        if (!asJson)
        {
            AnsiConsole.MarkupLine($"[grey]Using server:[/] {server}");
            AnsiConsole.MarkupLine("Authorize Automaton CLI in your browser:");
            AnsiConsole.MarkupLine($"[link={authorization.VerificationUrl}]{authorization.VerificationUrl}[/]");
            AnsiConsole.MarkupLine($"Enter code: [bold]{authorization.UserCode}[/]");
        }

        var browserOpened = !forceDevice && CliBrowserService.TryOpenUrl(authorization.VerificationUrl);
        if (!browserOpened && !asJson)
        {
            AnsiConsole.MarkupLine("[yellow]A browser could not be opened. Open the URL above on another device.[/]");
        }

        if (!asJson)
            AnsiConsole.MarkupLine("[grey]Waiting for authorization...[/]");

        var deadline = DateTime.UtcNow.AddSeconds(authorization.ExpiresIn);
        string? apiKey = null;
        while (DateTime.UtcNow < deadline)
        {
            var status = await client.PollCliAuthorizationAsync(authorization.RequestId, authorization.Verifier);
            var state = status.TryGetProperty("status", out var stateProperty) ? stateProperty.GetString() : null;
            if (string.Equals(state, "authorized", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = status.GetProperty("apiKey").GetString();
                break;
            }

            if (state is "expired" or "invalid" or "completed")
                throw new InvalidOperationException("The CLI authorization request is no longer available.");

            if (state == "cancelled")
                throw new InvalidOperationException("Authorization was cancelled in Studio.");

            await Task.Delay(TimeSpan.FromSeconds(authorization.PollInterval));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new TimeoutException("The CLI authorization request expired before it was approved.");

        using var authenticatedClient = new AutomatonApiClient(server, apiKey);
        await SaveCredentialsAsync(
            config,
            cwd,
            server,
            apiKey,
            global,
            new PendingCliAuthorizationCompletion
            {
                RequestId = authorization.RequestId,
                Server = server,
                Verifier = authorization.Verifier
            });

        var completionService = new CliAuthorizationCompletionService(config);
        if (await completionService.TryCompletePendingAsync(cwd, global))
        {
            // The completion service clears the pending state after the server acknowledges delivery.
        }
        else
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.Authentication,
                true,
                "credential saved; server acknowledgement will be retried on the next CLI invocation",
                IsWarning: true));
            if (!asJson)
                AnsiConsole.MarkupLine("[yellow]⚠[/] Credential saved; server acknowledgement will be retried on the next CLI invocation.");
        }

        string displayName;
        try
        {
            var user = await authenticatedClient.GetUserProfileAsync();
            displayName = GetUserDisplayName(user);
        }
        catch (Exception ex)
        {
            displayName = "Automaton user";
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true,
                "credential saved; profile details could not be loaded", true));
            if (!asJson)
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Signed in, but profile details could not be loaded: {Markup.Escape(ex.Message)}");
        }

        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, true, "browser/device authorization completed"));
        if (!checks.Any(x => x.Name == CommandDiagnosticCheckNames.UserProfile))
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {displayName}"));

        if (asJson)
        {
            CliJson.Write(new
            {
                success = true,
                summary = "Login complete",
                checks = checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = Array.Empty<string>(),
                authMode = "cliDeviceAuthorization",
                storage = global ? "global" : "local",
                server,
                user = new { name = displayName }
            });
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Signed in as [bold]{Markup.Escape(displayName)}[/]");
        AnsiConsole.MarkupLine("[green]✓[/] Automaton CLI authorized");
        AnsiConsole.MarkupLine(global
            ? "[grey]✓ Credential saved securely to ~/.automaton/config.json[/]"
            : "[grey]✓ Credential saved securely to ./.automaton/config.json[/]");
    }

    private static async Task LoginWithExistingKeyAsync(
        ConfigService config,
        string cwd,
        string server,
        string key,
        bool global,
        bool asJson,
        List<CommandDiagnosticCheck> checks)
    {
        using var client = new AutomatonApiClient(server, key);
        var user = await client.GetUserProfileAsync();
        await SaveCredentialsAsync(config, cwd, server, key, global);

        var displayName = GetUserDisplayName(user);
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, true, $"credentials validated against {server}"));
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {displayName}"));
        var result = new CommandDiagnosticResult { Success = true, Summary = "Login complete", Checks = checks, NextSteps = [] };

        if (asJson)
        {
            CliJson.Write(new
            {
                success = true,
                summary = result.Summary,
                checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = result.NextSteps,
                authMode = "apiKey",
                storage = global ? "global" : "local",
                server,
                user = new { name = displayName }
            });
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Authenticated as [bold]{Markup.Escape(displayName)}[/]");
        AnsiConsole.MarkupLine(global
            ? "[grey]Credentials saved to ~/.automaton/config.json[/]"
            : "[grey]Credentials saved to ./.automaton/config.json[/]");
    }

    private static async Task SaveCredentialsAsync(
        ConfigService config,
        string cwd,
        string server,
        string apiKey,
        bool global,
        PendingCliAuthorizationCompletion? pending = null)
    {
        var updated = global
            ? config.LoadGlobalConfig()
            : config.LoadProjectLocalConfig(cwd);
        updated.Server = server;
        updated.ApiKey = apiKey;
        updated.PendingAuthorizationCompletion = pending;
        if (global)
            config.SaveGlobalConfig(updated);
        else
            config.SaveProjectLocalConfig(cwd, updated);

        await Task.CompletedTask;
    }

    private static string GetUserDisplayName(System.Text.Json.JsonElement user)
    {
        if (user.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString()))
            return name.GetString()!;
        if (user.TryGetProperty("email", out var email) && !string.IsNullOrWhiteSpace(email.GetString()))
            return email.GetString()!;
        return "Automaton user";
    }

    private static string PromptForApiKey()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("API key")
                .Secret());
    }
}
