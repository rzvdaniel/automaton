using Automaton.Cli.Models;
using System.Text.Json;

namespace Automaton.Cli.Services;

public sealed class SetupService
{
    public const string DefaultHostedServer = "https://automaton.monster";

    private readonly ConfigService _config;
    private readonly Func<string, string, CancellationToken, Task<JsonElement>> _getUserProfileAsync;

    public SetupService(
        ConfigService config,
        Func<string, string, CancellationToken, Task<JsonElement>>? getUserProfileAsync = null)
    {
        _config = config;
        _getUserProfileAsync = getUserProfileAsync ?? GetUserProfileAsync;
    }

    public async Task<SetupResult> SetupAsync(
        string directory,
        string? server,
        string? apiKey,
        bool global,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var resolvedServer = string.IsNullOrWhiteSpace(server)
            ? DefaultHostedServer
            : server.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials, false, "API key is required to complete setup"));
            nextSteps.Add("Provide --key or answer the API key prompt");
            return new SetupResult
            {
                Success = false,
                Summary = "Setup failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = resolvedServer,
                Storage = global ? "global" : "local"
            };
        }

        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials, true, $"using {resolvedServer}"));

        try
        {
            var user = await _getUserProfileAsync(resolvedServer, apiKey, cancellationToken);
            var userName = user.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
            var email = user.TryGetProperty("email", out var emailProperty) ? emailProperty.GetString() : null;
            var userId = user.TryGetProperty("id", out var idProperty) ? idProperty.GetGuid().ToString() : null;

            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, true, $"credentials validated against {resolvedServer}"));
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {userName ?? email ?? userId}"));

            var updated = new GlobalConfig { Server = resolvedServer, ApiKey = apiKey };
            if (global)
            {
                _config.SaveGlobalConfig(updated);
            }
            else
            {
                _config.SaveProjectLocalConfig(directory, updated);
            }

            nextSteps.Add("Run automaton new \"My App\" or automaton link <app-id>");

            return new SetupResult
            {
                Success = true,
                Summary = "Setup complete",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = resolvedServer,
                Storage = global ? "global" : "local",
                UserId = userId,
                UserName = userName,
                UserEmail = email
            };
        }
        catch (Exception ex)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, false, ex.Message));
            nextSteps.Add("Verify the API key, then rerun automaton setup");
            return new SetupResult
            {
                Success = false,
                Summary = "Setup failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = resolvedServer,
                Storage = global ? "global" : "local"
            };
        }
    }

    private static async Task<JsonElement> GetUserProfileAsync(string server, string apiKey, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.GetUserProfileAsync(cancellationToken);
    }
}