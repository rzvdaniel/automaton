using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class WhoAmICommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");

        var cmd = new Command("whoami", "Show the current authenticated Automaton user")
        {
            jsonOption,
            environmentOption
        };

        var handler = new WhoAmIHandler(config);
        cmd.SetHandler((bool asJson, string? environmentName) => handler.ExecuteAsync(asJson, environmentName), jsonOption, environmentOption);

        return cmd;
    }
}

internal sealed class WhoAmIHandler(ConfigService config)
{
    private readonly List<CommandDiagnosticCheck> checks = [];
    private readonly HashSet<string> nextSteps = new(StringComparer.OrdinalIgnoreCase);

    public async Task ExecuteAsync(bool asJson, string? environmentName)
    {
        try
        {
            var context = config.ResolveContext(directory: Directory.GetCurrentDirectory(), environmentName: environmentName);
            var credentials = ValidateCredentials(context.Server, context.ApiKey);
            if (!credentials)
            {
                WriteFailure(asJson, "Whoami failed");
                return;
            }

            using var client = new AutomatonApiClient(context.Server!, context.ApiKey!);
            var profile = await client.GetUserProfileAsync();
            var user = ReadUser(profile);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {user.Name ?? user.Email ?? user.Id}"));
            WriteSuccess(asJson, context.EnvironmentName, context.Server!, user);
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, false, exception.Message));
            nextSteps.Add("Run automaton login to refresh or set credentials");
            WriteFailure(asJson, "Whoami failed");
        }
    }

    private bool ValidateCredentials(string? server, string? apiKey)
    {
        var check = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(check);
        return check.Success && !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(apiKey);
    }

    private static UserProfile ReadUser(JsonElement user)
    {
        var id = user.TryGetProperty("id", out var idProperty) ? idProperty.GetGuid().ToString() : null;
        var name = GetString(user, "name");
        var email = GetString(user, "email");
        var publicUsername = GetString(user, "publicUsername");
        var roles = user.TryGetProperty("roles", out var rolesProperty) && rolesProperty.ValueKind == JsonValueKind.Array
            ? rolesProperty.EnumerateArray().Select(role => role.GetString()).Where(role => !string.IsNullOrWhiteSpace(role)).ToArray()
            : Array.Empty<string?>();
        return new UserProfile(id, name, email, publicUsername, roles);
    }

    private static string? GetString(JsonElement user, string propertyName) =>
        user.TryGetProperty(propertyName, out var property) ? property.GetString() : null;

    private void WriteFailure(bool asJson, string summary)
    {
        Environment.ExitCode = 1;
        WriteDiagnostics(asJson, new CommandDiagnosticResult { Success = false, Summary = summary, Checks = checks, NextSteps = nextSteps.ToList() });
    }

    private static void WriteDiagnostics(bool asJson, CommandDiagnosticResult result)
    {
        if (asJson)
        {
            CliJson.Write(new { success = result.Success, summary = result.Summary, checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = result.NextSteps });
            return;
        }

        CliDiagnosticsWriter.Write(result);
    }

    private void WriteSuccess(bool asJson, string? environmentName, string server, UserProfile user)
    {
        var result = new CommandDiagnosticResult { Success = true, Summary = "Whoami complete", Checks = checks, NextSteps = [] };
        if (asJson)
        {
            CliJson.Write(new { success = true, summary = result.Summary, checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = result.NextSteps, environment = environmentName, authMode = "apiKey", server, user = new { id = user.Id, name = user.Name, email = user.Email, publicUsername = user.PublicUsername, roles = user.Roles } });
            return;
        }

        var table = new Table();
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("environment", string.IsNullOrWhiteSpace(environmentName) ? "-" : environmentName);
        table.AddRow("server", server);
        table.AddRow("auth", "apiKey");
        table.AddRow("id", user.Id ?? "?");
        table.AddRow("name", user.Name ?? "?");
        table.AddRow("email", user.Email ?? "?");
        table.AddRow("public username", string.IsNullOrWhiteSpace(user.PublicUsername) ? "-" : user.PublicUsername);
        table.AddRow("roles", user.Roles.Length == 0 ? "-" : string.Join(", ", user.Roles!));
        AnsiConsole.Write(table);
    }

    private sealed record UserProfile(string? Id, string? Name, string? Email, string? PublicUsername, string?[] Roles);
}
