using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public static class CliCommandChecks
{
    public static CommandDiagnosticCheck ValidateProjectLink(
        bool configFileExists,
        Models.AutomatonConfig? projectConfig,
        string? resolvedAppId,
        ISet<string> nextSteps)
    {
        if (!configFileExists)
        {
            nextSteps.Add("Run automaton new \"My App\" or automaton link <app-id>");
            return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AppLink, false, "automaton.json not found in the project root");
        }

        if (projectConfig == null)
        {
            nextSteps.Add("Fix the automaton.json file so it contains valid JSON");
            return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AppLink, false, "automaton.json is invalid or unreadable");
        }

        if (string.IsNullOrWhiteSpace(resolvedAppId))
        {
            nextSteps.Add("Run automaton create or automaton link <app-id> with --env <name> to link an environment-specific remote app");
            return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AppLink, false, "no appId is configured for the selected environment");
        }

        return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AppLink, true, $"linked app {resolvedAppId}");
    }

    public static CommandDiagnosticCheck ValidateCredentials(string? server, string? apiKey, ISet<string> nextSteps)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            nextSteps.Add("Run automaton login to configure the hosted server and credentials");
            return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials, false, "server could not be resolved from environment, automaton.json, or CLI config");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            nextSteps.Add("Run automaton login");
            return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials, false, "API key could not be resolved from environment or CLI config");
        }

        return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials, true, $"credentials resolved for {server}");
    }
}
