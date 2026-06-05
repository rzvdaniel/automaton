using Automaton.Cli.Models;
using Spectre.Console;
using System.Text.Json;

namespace Automaton.Cli.Services;

public sealed class PublishService
{
    private readonly ConfigService _config;

    public PublishService(ConfigService config)
    {
        _config = config;
    }

    public async Task<PublishResult> PublishAsync(
        string directory,
        string? slug,
        bool? requiresAuthentication,
        bool? isForkable,
        bool yes,
        bool nonInteractive = false,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var appConfig = _config.TryLoadProjectConfig(directory);
            var (server, apiKey) = _config.ResolveCredentials(appConfig, directory);
            var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(directory, "automaton.json")), appConfig, nextSteps);
            var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
            checks.Add(appLinkCheck);
            checks.Add(credentialsCheck);

            if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
            {
                return new PublishResult
                {
                    Success = false,
                    Summary = "Publish failed",
                    Checks = checks,
                    NextSteps = nextSteps.ToList(),
                    AppId = appConfig?.AppId,
                    AppName = appConfig?.Name
                };
            }

            using var client = new AutomatonApiClient(server!, apiKey!);
            var app = await client.GetAppAsync(appConfig.AppId, cancellationToken);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, true, $"remote app loaded: {appConfig.AppId}"));

            var resolvedSlug = !string.IsNullOrWhiteSpace(slug)
                ? slug.Trim()
                : TryReadString(app, "publicUrl");
            var resolvedRequiresAuthentication = requiresAuthentication ?? TryReadBool(app, "requiresAuthentication") ?? false;
            var resolvedIsForkable = isForkable ?? TryReadBool(app, "isForkable") ?? false;

            if (string.IsNullOrWhiteSpace(resolvedSlug))
            {
                if (yes || nonInteractive)
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, false, "public URL slug is required for first publish"));
                    nextSteps.Add("Rerun automaton publish --slug <your-app-slug>");
                    return new PublishResult
                    {
                        Success = false,
                        Summary = "Publish failed",
                        Checks = checks,
                        NextSteps = nextSteps.ToList(),
                        AppId = appConfig.AppId,
                        AppName = appConfig.Name,
                        RequiresAuthentication = resolvedRequiresAuthentication,
                        IsForkable = resolvedIsForkable
                    };
                }

                resolvedSlug = PromptForSlug(appConfig.Name);
            }

            if (!yes)
            {
                if (nonInteractive)
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.PublishApproval, false, "publish requires --yes in non-interactive mode"));
                    return new PublishResult
                    {
                        Success = false,
                        Summary = "Publish failed",
                        Checks = checks,
                        NextSteps = ["Rerun automaton publish --yes once you intend to make the app public"],
                        AppId = appConfig.AppId,
                        AppName = appConfig.Name,
                        Slug = resolvedSlug,
                        Url = CliAppUrlBuilder.BuildPublishedUrl(server!, resolvedSlug),
                        RequiresAuthentication = resolvedRequiresAuthentication,
                        IsForkable = resolvedIsForkable
                    };
                }

                Console.Write($"Publish '{appConfig.Name}' at {CliAppUrlBuilder.BuildPublishedUrl(server!, resolvedSlug)}? [y/N]: ");
                var confirmation = Console.ReadLine()?.Trim();
                if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.PublishApproval, false, "publish cancelled by user"));
                    return new PublishResult
                    {
                        Success = false,
                        Cancelled = true,
                        Summary = "Publish cancelled",
                        Checks = checks,
                        NextSteps = ["Rerun automaton publish --yes when you are ready to make the app public"],
                        AppId = appConfig.AppId,
                        AppName = appConfig.Name,
                        Slug = resolvedSlug,
                        Url = CliAppUrlBuilder.BuildPublishedUrl(server!, resolvedSlug),
                        RequiresAuthentication = resolvedRequiresAuthentication,
                        IsForkable = resolvedIsForkable
                    };
                }
            }

            await client.PublishAppAsync(
                appConfig.AppId,
                resolvedSlug,
                resolvedRequiresAuthentication,
                executionRunnerId: null,
                category: null,
                tags: null,
                resolvedIsForkable,
                cancellationToken);

            var publishedUrl = CliAppUrlBuilder.BuildPublishedUrl(server!, resolvedSlug);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, true, $"published at {publishedUrl}"));

            return new PublishResult
            {
                Success = true,
                Summary = "Publish complete",
                Checks = checks,
                NextSteps = [],
                AppId = appConfig.AppId,
                AppName = appConfig.Name,
                Slug = resolvedSlug,
                Url = publishedUrl,
                RequiresAuthentication = resolvedRequiresAuthentication,
                IsForkable = resolvedIsForkable
            };
        }
        catch (Exception ex)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, false, ex.Message));
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            return new PublishResult
            {
                Success = false,
                Summary = "Publish failed",
                Checks = checks,
                NextSteps = nextSteps.ToList()
            };
        }
    }

    private static string PromptForSlug(string? appName)
    {
        var defaultSlug = Slugify(appName);
        var prompt = new TextPrompt<string>("Choose a public URL slug")
            .DefaultValue(defaultSlug)
            .PromptStyle("green")
            .ValidationErrorMessage("[red]Use lowercase letters, numbers, and hyphens only[/]")
            .Validate(value => IsValidSlug(value)
                ? Spectre.Console.ValidationResult.Success()
                : Spectre.Console.ValidationResult.Error("[red]Use lowercase letters, numbers, and hyphens only[/]"));

        return AnsiConsole.Prompt(prompt).Trim();
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "app";
        }

        var slug = value.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "app" : slug;
    }

    private static bool IsValidSlug(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^[a-z0-9\\-]+$");
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool? TryReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            ? property.GetBoolean()
            : null;
    }
}
