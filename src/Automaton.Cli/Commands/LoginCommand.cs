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
        var emailOption = new Option<string?>("--email",
            "Sign in with this email; the password is read from standard input (no browser). Creates and saves a developer key");
        var permissionsAdminOption = new Option<bool>("--permissions-admin",
            "Ask a system administrator to approve in Studio an admin key that only approves app permissions; saved as profile '<profile>-permissions'");

        var cmd = new Command("login", "Authorize the Automaton CLI or validate an existing API key")
        {
            serverOption,
            keyOption,
            deviceOption,
            localOption,
            jsonOption,
            permissionsAdminOption,
            emailOption
        };

        cmd.SetHandler((string? server, string? key, bool device, bool local, bool asJson, bool permissionsAdmin, string? email) =>
            HandleLoginAsync(config, server, key, device, local, asJson, permissionsAdmin, email),
            serverOption, keyOption, deviceOption, localOption, jsonOption, permissionsAdminOption, emailOption);

        return cmd;
    }

    private static async Task HandleLoginAsync(
        ConfigService config, string? server, string? key, bool device, bool local, bool asJson, bool permissionsAdmin = false, string? email = null)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (permissionsAdmin && (local || !string.IsNullOrWhiteSpace(key) || HasKeyArgument()))
                throw new InvalidOperationException("--permissions-admin saves its own profile and cannot be combined with --local or --key.");
            var cwd = Directory.GetCurrentDirectory();
            var global = !local;
            var existing = config.ResolveExistingConfig(global, cwd);
            server = ResolveServer(server, existing.Server);
            if (global)
            {
                var name = config.ProfileOverride ?? Environment.GetEnvironmentVariable("AUTOMATON_PROFILE")
                    ?? ConfigService.ProfileNameFromServer(server);
                if (permissionsAdmin) name = PermissionsProfileName(name);
                var profiles = config.LoadProfilesConfig();
                if (profiles.Profiles.TryGetValue(name, out var saved) && saved.Server != null
                    && !string.Equals(saved.Server.TrimEnd('/'), server, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Profile '{name}' already uses {saved.Server}; choose another profile name.");
                config.ProfileOverride = name;
                existing = config.ResolveExistingConfig(global, cwd);
            }
            if (!string.IsNullOrWhiteSpace(email))
            {
                if (permissionsAdmin || !string.IsNullOrWhiteSpace(key) || HasKeyArgument())
                    throw new InvalidOperationException("--email cannot be combined with --key or --permissions-admin.");
                key = await CreateKeyWithPasswordAsync(server, email.Trim(), Console.In, Console.IsInputRedirected);
            }
            await CompletePendingAuthorizationAsync(config, cwd, global, asJson, existing, checks);
            if (string.IsNullOrWhiteSpace(email)) key = ResolveRequestedKey(key);
            await CompleteLoginAsync(config, cwd, server, key, global, device, asJson, checks, permissionsAdmin);
        }
        catch (Exception ex)
        {
            WriteLoginFailure(ex, asJson, checks, nextSteps);
        }
    }

    /// <summary>Password sign-in for an installing agent: the password comes from standard input, never an argument.</summary>
    internal static async Task<string> CreateKeyWithPasswordAsync(string server, string email, TextReader input, bool redirected)
    {
        if (!redirected)
            throw new InvalidOperationException("Pipe the password on standard input, for example: automaton login --server URL --email E < password-file");
        var password = input.ReadToEnd().TrimEnd('\r', '\n');
        if (password.Length == 0) throw new InvalidOperationException("No password was read from standard input.");
        using var client = new AutomatonApiClient(server);
        var token = await client.SignInWithPasswordAsync(email, password);
        var created = await client.CreateApiKeyWithAccessTokenAsync(token, $"CLI {Environment.MachineName}", ApiKeyScopes.Developer);
        return created.GetProperty("apiKey").GetString()
            ?? throw new InvalidOperationException("The server did not return an API key.");
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
        List<CommandDiagnosticCheck> checks,
        bool permissionsAdmin = false)
        => !string.IsNullOrWhiteSpace(key)
            ? LoginWithExistingKeyAsync(config, cwd, server, key, global, asJson, checks)
            : LoginWithDeviceAuthorizationAsync(config, cwd, server, global, device, asJson, checks, permissionsAdmin);

    internal static string PermissionsProfileName(string profile)
        => profile.EndsWith("-permissions", StringComparison.OrdinalIgnoreCase) ? profile : profile + "-permissions";

    private static bool HasKeyArgument()
        => Environment.GetCommandLineArgs().Any(a => a.Equals("--key", StringComparison.OrdinalIgnoreCase)
            || a.Equals("-k", StringComparison.OrdinalIgnoreCase));

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
        List<CommandDiagnosticCheck> checks,
        bool permissionsAdmin = false)
    {
        using var client = new AutomatonApiClient(server);
        var authorization = await client.CreateCliAuthorizationRequestAsync(
            Environment.MachineName, default, permissionsAdmin ? "PermissionsAdmin" : null);
        DisplayAuthorizationRequest(server, authorization, forceDevice, asJson, permissionsAdmin);
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
        // The admin key is refused on the profile endpoint by design, so do not ask for it.
        var displayName = permissionsAdmin
            ? "system administrator (permissions key)"
            : await LoadAuthenticatedDisplayNameAsync(authenticatedClient, asJson, checks);
        checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.Authentication, true, "browser/device authorization completed"));
        AddProfileCheck(checks, displayName);
        WriteDeviceLoginSuccess(server, global, asJson, displayName, checks, StoragePath(config, cwd, global));
    }

    private static void DisplayAuthorizationRequest(
        string server, CliAuthorizationResponse authorization, bool forceDevice, bool asJson, bool permissionsAdmin = false)
    {
        if (asJson) return;
        AnsiConsole.MarkupLine($"[grey]Using server:[/] {server}");
        AnsiConsole.MarkupLine(permissionsAdmin
            ? "A system administrator must approve an [bold]admin key for app permissions[/] in Studio (it can only approve or reject app permissions):"
            : "Authorize Automaton CLI in your browser:");
        // Plain Console so long URLs and codes are never wrapped at the terminal width.
        Console.WriteLine(authorization.VerificationUrl);
        var browserOpened = !forceDevice && CliBrowserService.TryOpenUrl(authorization.VerificationUrl);
        WriteVerificationCode(authorization.UserCode, forceDevice, browserOpened);
        AnsiConsole.MarkupLine("[grey]Waiting for authorization...[/]");
    }

    private static void WriteVerificationCode(string userCode, bool forceDevice, bool browserOpened)
    {
        if (browserOpened)
        {
            Console.WriteLine($"Confirm this code in Studio: {userCode}");
            return;
        }
        if (forceDevice)
        {
            Console.WriteLine($"Open the URL above and enter this code: {userCode}");
            return;
        }
        AnsiConsole.MarkupLine("[yellow]A browser could not be opened. Open the URL above on another device.[/]");
        Console.WriteLine($"Enter this code: {userCode}");
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
        if (state == "denied")
        {
            throw new InvalidOperationException("The approver is no longer a system administrator; no key was created.");
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
        IEnumerable<CommandDiagnosticCheck> checks,
        string storagePath)
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
        AnsiConsole.MarkupLine(DeviceSavedMessage(storagePath));
    }

    /// <summary>The file credentials are written to: the shared config (AUTOMATON_CONFIG_DIR aware) or this project's.</summary>
    internal static string StoragePath(ConfigService config, string cwd, bool global) =>
        global ? config.GlobalConfigPath : config.GetProjectLocalConfigPath(cwd);

    internal static string KeySavedMessage(string path) => $"[grey]Credentials saved to {Markup.Escape(path)}[/]";

    internal static string DeviceSavedMessage(string path) => $"[grey]✓ Credential saved securely to {Markup.Escape(path)}[/]";

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
        AnsiConsole.MarkupLine(KeySavedMessage(StoragePath(config, cwd, global)));
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
