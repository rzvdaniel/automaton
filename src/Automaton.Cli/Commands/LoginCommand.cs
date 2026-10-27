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

        cmd.SetHandler((string? server, string? key, bool device, bool local, bool asJson) =>
            HandleLoginAsync(config, server, key, device, local, asJson),
            serverOption, keyOption, deviceOption, localOption, jsonOption);

        return cmd;
    }

    private static async Task HandleLoginAsync(
        ConfigService config, string? server, string? key, bool device, bool local, bool asJson)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            var global = !local;
            var existing = config.ResolveExistingConfig(global, cwd);
            server = ResolveServer(server, existing.Server);
            await CompletePendingAuthorizationAsync(config, cwd, global, asJson, existing, checks);
            key = ResolveRequestedKey(key);
            await CompleteLoginAsync(config, cwd, server, key, global, device, asJson, checks);
        }
        catch (Exception ex)
        {
            WriteLoginFailure(ex, asJson, checks, nextSteps);
        }
    }

    private static string ResolveServer(string? server, string? savedServer)
        => string.IsNullOrWhiteSpace(server)
            ? savedServer ?? ConfigService.DefaultHostedServer
            : server.TrimEnd('/');

    private static async Task CompletePendingAuthorizationAsync(
        ConfigService config,
        string cwd,
        bool global,
        bool asJson,
        GlobalConfig existing,
        ICollection<CommandDiagnosticCheck> checks)
    {
        if (existing.PendingAuthorizationCompletion == null) return;
        var completionService = new CliAuthorizationCompletionService(config);
        if (await completionService.TryCompletePendingAsync(cwd, global)) return;
        checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.Authentication,
            true,
            "credential is saved; server acknowledgement remains pending and will be retried automatically",
            IsWarning: true));
        if (!asJson)
        {
            Console.Error.WriteLine("Warning: CLI credential is saved, but server acknowledgement is pending. Automaton will retry automatically.");
        }
    }

    private static string? ResolveRequestedKey(string? key)
    {
        var keyWasRequested = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--key", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "-k", StringComparison.OrdinalIgnoreCase));
        return keyWasRequested && string.IsNullOrWhiteSpace(key) ? PromptForApiKey() : key;
    }

    private static Task CompleteLoginAsync(
        ConfigService config,
        string cwd,
        string server,
        string? key,
        bool global,
        bool device,
        bool asJson,
        List<CommandDiagnosticCheck> checks)
        => !string.IsNullOrWhiteSpace(key)
            ? LoginWithExistingKeyAsync(config, cwd, server, key, global, asJson, checks)
            : LoginWithDeviceAuthorizationAsync(config, cwd, server, global, device, asJson, checks);

    private static void WriteLoginFailure(
        Exception exception,
        bool asJson,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, false, exception.Message));
        nextSteps.Add("Run automaton login to start browser/device authorization");
        var failure = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Login failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList()
        };
        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = failure.Summary,
                checks = failure.Checks.Select(check => new
                {
                    name = check.Name,
                    success = check.Success,
                    warning = check.IsWarning,
                    error = check.Message
                }),
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
        DisplayAuthorizationRequest(server, authorization, forceDevice, asJson);
        var apiKey = await PollForApiKeyAsync(client, authorization);

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

        await CompleteAuthorizationAsync(config, cwd, global, asJson, checks);
        var displayName = await LoadAuthenticatedDisplayNameAsync(authenticatedClient, asJson, checks);
        checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.Authentication, true, "browser/device authorization completed"));
        AddProfileCheck(checks, displayName);
        WriteDeviceLoginSuccess(server, global, asJson, displayName, checks);
    }

    private static void DisplayAuthorizationRequest(
        string server, CliAuthorizationResponse authorization, bool forceDevice, bool asJson)
    {
        if (asJson) return;
        AnsiConsole.MarkupLine($"[grey]Using server:[/] {server}");
        AnsiConsole.MarkupLine("Authorize Automaton CLI in your browser:");
        AnsiConsole.MarkupLine($"[link={authorization.VerificationUrl}]{authorization.VerificationUrl}[/]");
        var browserOpened = !forceDevice && CliBrowserService.TryOpenUrl(authorization.VerificationUrl);
        WriteVerificationCode(authorization.UserCode, forceDevice, browserOpened);
        AnsiConsole.MarkupLine("[grey]Waiting for authorization...[/]");
    }

    private static void WriteVerificationCode(string userCode, bool forceDevice, bool browserOpened)
    {
        if (browserOpened)
        {
            AnsiConsole.MarkupLine($"Confirm this code in Studio: [bold]{userCode}[/]");
            return;
        }
        if (forceDevice)
        {
            AnsiConsole.MarkupLine($"Open the URL above and enter this code: [bold]{userCode}[/]");
            return;
        }
        AnsiConsole.MarkupLine("[yellow]A browser could not be opened. Open the URL above on another device.[/]");
        AnsiConsole.MarkupLine($"Enter this code: [bold]{userCode}[/]");
    }

    internal static async Task<string> PollForApiKeyAsync(
        AutomatonApiClient client, CliAuthorizationResponse authorization)
    {
        using var expiry = new CancellationTokenSource(TimeSpan.FromSeconds(authorization.ExpiresIn));
        try
        {
            while (true)
            {
                var status = await client.PollCliAuthorizationAsync(
                    authorization.RequestId, authorization.Verifier, expiry.Token);
                var state = status.TryGetProperty("status", out var property) ? property.GetString() : null;
                var apiKey = ReadAuthorizedApiKey(status, state);
                if (apiKey != null) return apiKey;
                await HandleAuthorizationStateAsync(state, authorization.PollInterval, expiry.Token);
            }
        }
        catch (OperationCanceledException) when (expiry.IsCancellationRequested)
        {
            throw new TimeoutException("The CLI authorization request expired before it was approved.");
        }
    }

    private static string? ReadAuthorizedApiKey(System.Text.Json.JsonElement status, string? state)
    {
        if (!string.Equals(state, "authorized", StringComparison.OrdinalIgnoreCase)) return null;
        return status.GetProperty("apiKey").GetString();
    }

    private static Task HandleAuthorizationStateAsync(string? state, int pollInterval, CancellationToken cancellationToken)
    {
        if (state is "expired" or "invalid" or "completed")
        {
            throw new InvalidOperationException("The CLI authorization request is no longer available.");
        }
        if (state == "cancelled")
        {
            throw new InvalidOperationException("Authorization was cancelled in Studio.");
        }
        return Task.Delay(TimeSpan.FromSeconds(pollInterval), cancellationToken);
    }

    private static async Task CompleteAuthorizationAsync(
        ConfigService config,
        string cwd,
        bool global,
        bool asJson,
        ICollection<CommandDiagnosticCheck> checks)
    {
        var completionService = new CliAuthorizationCompletionService(config);
        if (await completionService.TryCompletePendingAsync(cwd, global)) return;
        checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.Authentication,
            true,
            "credential saved; server acknowledgement will be retried on the next CLI invocation",
            IsWarning: true));
        if (!asJson)
        {
            AnsiConsole.MarkupLine("[yellow]⚠[/] Credential saved; server acknowledgement will be retried on the next CLI invocation.");
        }
    }

    private static async Task<string> LoadAuthenticatedDisplayNameAsync(
        AutomatonApiClient client,
        bool asJson,
        ICollection<CommandDiagnosticCheck> checks)
    {
        try
        {
            var user = await client.GetUserProfileAsync();
            return GetUserDisplayName(user);
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.UserProfile, true,
                "credential saved; profile details could not be loaded", true));
            if (!asJson)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Signed in, but profile details could not be loaded: {Markup.Escape(exception.Message)}");
            }
            return "Automaton user";
        }
    }

    private static void AddProfileCheck(ICollection<CommandDiagnosticCheck> checks, string displayName)
    {
        if (!checks.Any(check => check.Name == CommandDiagnosticCheckNames.UserProfile))
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {displayName}"));
        }
    }

    private static void WriteDeviceLoginSuccess(
        string server,
        bool global,
        bool asJson,
        string displayName,
        IEnumerable<CommandDiagnosticCheck> checks)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = true,
                summary = "Login complete",
                checks = checks.Select(check => new
                {
                    name = check.Name,
                    success = check.Success,
                    warning = check.IsWarning,
                    error = check.Message
                }),
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
