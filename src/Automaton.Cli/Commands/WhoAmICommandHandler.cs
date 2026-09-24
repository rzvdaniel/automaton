using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.Text.Json;

namespace Automaton.Cli.Commands;

internal static class WhoAmICommandHandler
{
    internal static async Task HandleAsync(ConfigService config, bool asJson, string? environmentName)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var context = config.ResolveContext(
                directory: Directory.GetCurrentDirectory(), environmentName: environmentName);
            if (!HasCredentials(context.Server, context.ApiKey, checks, nextSteps))
            {
                WriteFailure(checks, nextSteps, asJson);
                Environment.ExitCode = 1;
                return;
            }

            var user = await LoadUserAsync(context.Server!, context.ApiKey!);
            var result = ReadUser(user);
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.UserProfile, true,
                $"authenticated as {result.Name ?? result.Email ?? result.Id}"));
            WriteSuccess(context.EnvironmentName, context.Server!, result, checks, asJson);
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.UserProfile, false, exception.Message));
            nextSteps.Add("Run automaton login to refresh or set credentials");
            WriteFailure(checks, nextSteps, asJson);
            Environment.ExitCode = 1;
        }
    }

    private static bool HasCredentials(
        string? server,
        string? apiKey,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var check = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(check);
        return check.Success && !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(apiKey);
    }

    private static async Task<JsonElement> LoadUserAsync(string server, string apiKey)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.GetUserProfileAsync();
    }

    private static UserProfile ReadUser(JsonElement user)
    {
        var id = user.TryGetProperty("id", out var idProperty)
            ? idProperty.GetGuid().ToString()
            : null;
        var name = ReadString(user, "name");
        var email = ReadString(user, "email");
        var username = ReadString(user, "publicUsername");
        var roles = ReadRoles(user);
        return new UserProfile(id, name, email, username, roles);
    }

    private static string? ReadString(JsonElement value, string property)
        => value.TryGetProperty(property, out var result) ? result.GetString() : null;

    private static string?[] ReadRoles(JsonElement user)
    {
        if (!user.TryGetProperty("roles", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(role => role.GetString())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .ToArray();
    }

    private static void WriteFailure(
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        bool asJson)
    {
        var failure = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Whoami failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList()
        };
        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = failure.Summary,
                checks = MapChecks(failure.Checks),
                nextSteps = failure.NextSteps
            });
            return;
        }

        CliDiagnosticsWriter.Write(failure);
    }

    private static void WriteSuccess(
        string? environmentName,
        string server,
        UserProfile user,
        IReadOnlyCollection<CommandDiagnosticCheck> checks,
        bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = true,
                summary = "Whoami complete",
                checks = MapChecks(checks),
                nextSteps = Array.Empty<string>(),
                environment = environmentName,
                authMode = "apiKey",
                server,
                user = new
                {
                    user.Id,
                    user.Name,
                    user.Email,
                    publicUsername = user.PublicUsername,
                    user.Roles
                }
            });
            return;
        }

        var table = BuildUserTable(environmentName, server, user);
        AnsiConsole.Write(table);
    }

    private static Table BuildUserTable(string? environmentName, string server, UserProfile user)
    {
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
        table.AddRow("roles", user.Roles.Length == 0 ? "-" : string.Join(", ", user.Roles));
        return table;
    }

    private static IEnumerable<object> MapChecks(IEnumerable<CommandDiagnosticCheck> checks)
        => checks.Select(check => new
        {
            name = check.Name,
            success = check.Success,
            warning = check.IsWarning,
            error = check.Message
        });

    private sealed record UserProfile(
        string? Id, string? Name, string? Email, string? PublicUsername, string?[] Roles);
}
