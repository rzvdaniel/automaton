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
        var keyOption = new Option<string?>(["--key", "-k"], "Use an existing API key");
        keyOption.Arity = ArgumentArity.ZeroOrOne;
        var deviceOption = new Option<bool>("--device", "Do not open a browser; display the authorization URL and code");
        var localOption = new Option<bool>("--local", "Store credentials in the current project instead of ~/.automaton/config.json");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("login", "Authorize the Automaton CLI or validate an existing API key")
        {
            serverOption,
            keyOption,
            deviceOption,
            localOption,
            jsonOption
        };

        cmd.SetHandler(
            (string? server, string? key, bool device, bool local, bool asJson) =>
                HandleLoginAsync(config, server, key, device, local, asJson),
            serverOption,
            keyOption,
            deviceOption,
            localOption,
            jsonOption);

        return cmd;
    }

    private static async Task HandleLoginAsync(
        ConfigService config,
        string? requestedServer,
        string? key,
        bool device,
        bool local,
        bool asJson)
    {
        var checks = new List<CommandDiagnosticCheck>();
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            var global = !local;
            var server = await ResolveServerAsync(config, cwd, global, requestedServer, asJson, checks);
            key = ResolveRequestedKey(key);

            if (!string.IsNullOrWhiteSpace(key))
            {
                await LoginWithExistingKeyAsync(config, cwd, server, key, global, asJson, checks);
                return;
            }

            await LoginWithDeviceAuthorizationAsync(config, cwd, server, global, device, asJson, checks);
        }
        catch (Exception ex)
        {
            WriteLoginFailure(ex, checks, asJson);
        }
    }

    private static async Task<string> ResolveServerAsync(
        ConfigService config,
        string cwd,
        bool global,
        string? requestedServer,
        bool asJson,
        ICollection<CommandDiagnosticCheck> checks)
    {
        var existing = config.ResolveExistingConfig(global, cwd);
        var server = string.IsNullOrWhiteSpace(requestedServer)
            ? existing.Server ?? ConfigService.DefaultHostedServer
            : requestedServer.TrimEnd('/');

        if (existing.PendingAuthorizationCompletion is not null
            && !await new CliAuthorizationCompletionService(config).TryCompletePendingAsync(cwd, global))
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.Authentication,
                true,
                "credential is saved; server acknowledgement remains pending and will be retried automatically",
                IsWarning: true));
            if (!asJson)
                Console.Error.WriteLine("Warning: CLI credential is saved, but server acknowledgement is pending. Automaton will retry automatically.");
        }

        return server;
    }

    private static string? ResolveRequestedKey(string? key)
    {
        var keyWasRequested = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--key", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "-k", StringComparison.OrdinalIgnoreCase));
        return keyWasRequested && string.IsNullOrWhiteSpace(key) ? PromptForApiKey() : key;
    }

    private static void WriteLoginFailure(
        Exception exception,
        ICollection<CommandDiagnosticCheck> checks,
        bool asJson)
    {
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, false, exception.Message));
        var failure = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Login failed",
            Checks = checks.ToList(),
            NextSteps = ["Run automaton login to start browser/device authorization"]
        };

        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = failure.Summary,
                checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = failure.NextSteps
            });
        }
        else
        {
            CliDiagnosticsWriter.Write(failure);
        }

        Environment.ExitCode = 1;
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
        ShowAuthorizationInstructions(authorization, server, forceDevice, asJson);
        if (!asJson)
            AnsiConsole.MarkupLine("[grey]Waiting for authorization...[/]");

        var apiKey = await PollForApiKeyAsync(client, authorization);
        using var authenticatedClient = new AutomatonApiClient(server, apiKey);
        await SaveDeviceAuthorizationAsync(config, cwd, server, apiKey, global, authorization);
        await CompleteDeviceAuthorizationAsync(config, cwd, global, asJson, server, authenticatedClient, checks);
    }

    private static void ShowAuthorizationInstructions(
        CliAuthorizationResponse authorization,
        string server,
        bool forceDevice,
        bool asJson)
    {
        if (asJson)
            return;

        AnsiConsole.MarkupLine($"[grey]Using server:[/] {server}");
        AnsiConsole.MarkupLine("Authorize Automaton CLI in your browser:");
        AnsiConsole.MarkupLine($"[link={authorization.VerificationUrl}]{authorization.VerificationUrl}[/]");
        var browserOpened = !forceDevice && CliBrowserService.TryOpenUrl(authorization.VerificationUrl);
        if (browserOpened)
        {
            AnsiConsole.MarkupLine($"Confirm this code in Studio: [bold]{authorization.UserCode}[/]");
        }
        else if (forceDevice)
        {
            AnsiConsole.MarkupLine($"Open the URL above and enter this code: [bold]{authorization.UserCode}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]A browser could not be opened. Open the URL above on another device.[/]");
            AnsiConsole.MarkupLine($"Enter this code: [bold]{authorization.UserCode}[/]");
        }

    }

    private static async Task<string> PollForApiKeyAsync(
        AutomatonApiClient client,
        CliAuthorizationResponse authorization)
    {
        var deadline = DateTime.UtcNow.AddSeconds(authorization.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            var status = await client.PollCliAuthorizationAsync(authorization.RequestId, authorization.Verifier);
            var state = status.TryGetProperty("status", out var stateProperty) ? stateProperty.GetString() : null;
            if (string.Equals(state, "authorized", StringComparison.OrdinalIgnoreCase))
                return GetAuthorizedApiKey(status);
            ThrowIfAuthorizationEnded(state);
            await Task.Delay(TimeSpan.FromSeconds(authorization.PollInterval));
        }

        throw new TimeoutException("The CLI authorization request expired before it was approved.");
    }

    private static string GetAuthorizedApiKey(System.Text.Json.JsonElement status)
    {
        var apiKey = status.GetProperty("apiKey").GetString();
        return !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey
            : throw new TimeoutException("The CLI authorization request expired before it was approved.");
    }

    private static void ThrowIfAuthorizationEnded(string? state)
    {
        if (state is "expired" or "invalid" or "completed")
            throw new InvalidOperationException("The CLI authorization request is no longer available.");
        if (state == "cancelled")
            throw new InvalidOperationException("Authorization was cancelled in Studio.");
    }

    private static async Task SaveDeviceAuthorizationAsync(
        ConfigService config,
        string cwd,
        string server,
        string apiKey,
        bool global,
        CliAuthorizationResponse authorization)
    {
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
    }

    private static async Task CompleteDeviceAuthorizationAsync(
        ConfigService config,
        string cwd,
        bool global,
        bool asJson,
        string server,
        AutomatonApiClient authenticatedClient,
        List<CommandDiagnosticCheck> checks)
    {
        var completionService = new CliAuthorizationCompletionService(config);
        if (!await completionService.TryCompletePendingAsync(cwd, global))
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.Authentication,
                true,
                "credential saved; server acknowledgement will be retried on the next CLI invocation",
                IsWarning: true));
            if (!asJson)
                AnsiConsole.MarkupLine("[yellow]⚠[/] Credential saved; server acknowledgement will be retried on the next CLI invocation.");
        }

        var displayName = await GetDisplayNameAsync(authenticatedClient, asJson, checks);
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, true, "browser/device authorization completed"));
        if (!checks.Any(check => check.Name == CommandDiagnosticCheckNames.UserProfile))
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {displayName}"));

        WriteDeviceAuthorizationResult(checks, asJson, global, server, displayName);
    }

    private static async Task<string> GetDisplayNameAsync(
        AutomatonApiClient authenticatedClient,
        bool asJson,
        ICollection<CommandDiagnosticCheck> checks)
    {
        try
        {
            var user = await authenticatedClient.GetUserProfileAsync();
            return GetUserDisplayName(user);
        }
        catch (Exception ex)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true,
                "credential saved; profile details could not be loaded", true));
            if (!asJson)
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Signed in, but profile details could not be loaded: {Markup.Escape(ex.Message)}");
            return "Automaton user";
        }
    }

    private static void WriteDeviceAuthorizationResult(
        IReadOnlyList<CommandDiagnosticCheck> checks,
        bool asJson,
        bool global,
        string server,
        string displayName)
    {
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
