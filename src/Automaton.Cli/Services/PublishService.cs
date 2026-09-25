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
        string? environmentName = null,
        bool nonInteractive = false,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var contextResolution = ResolvePublishContext(directory, environmentName, checks, nextSteps);
            if (contextResolution.Failure != null) return contextResolution.Failure;
            var context = contextResolution.Context!;

            using var client = new AutomatonApiClient(context.Server, context.ApiKey);
            var app = await client.GetAppAsync(context.AppId, cancellationToken);
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.RemoteAccess, true, $"remote app loaded: {context.AppId}"));

            var resolved = ResolvePublishSettings(
                context, app, slug, requiresAuthentication, isForkable, yes, nonInteractive, checks, nextSteps);
            if (resolved.Failure != null) return resolved.Failure;
            var approvalFailure = ResolvePublishApproval(context, resolved.Settings!, yes, nonInteractive, checks);
            if (approvalFailure != null) return approvalFailure;

            await client.PublishAppAsync(
                context.AppId,
                resolved.Settings!.Slug,
                resolved.Settings.RequiresAuthentication,
                category: null,
                tags: null,
                resolved.Settings.IsForkable,
                cancellationToken);
            return CreatePublishSuccess(context, resolved.Settings, checks);
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
                NextSteps = nextSteps.ToList(),
                StudioAppUrl = string.Empty
            };
        }
    }

    private PublishContextResolution ResolvePublishContext(
        string directory,
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var appConfig = _config.TryLoadProjectConfig(directory);
        var resolved = _config.ResolveContext(appConfig, directory, environmentName);
        var appId = resolved.AppId;
        var appLinkCheck = CliCommandChecks.ValidateProjectLink(
            File.Exists(Path.Combine(directory, "automaton.json")), appConfig, appId, nextSteps);
        var credentialsCheck = CliCommandChecks.ValidateCredentials(
            resolved.Server, resolved.ApiKey, nextSteps);
        checks.Add(appLinkCheck);
        checks.Add(credentialsCheck);
        if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null ||
            string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            return new PublishContextResolution(null, new PublishResult
            {
                Success = false,
                Summary = "Publish failed",
                Checks = checks.ToList(),
                NextSteps = nextSteps.ToList(),
                AppId = appId,
                AppName = appConfig?.Name,
                StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(resolved.Server, appId)
            });
        }
        return new PublishContextResolution(
            new PublishContext(appConfig, resolved.Server, resolved.ApiKey, appId!), null);
    }

    private static PublishSettingsResult ResolvePublishSettings(
        PublishContext context,
        JsonElement app,
        string? slug,
        bool? requiresAuthentication,
        bool? isForkable,
        bool yes,
        bool nonInteractive,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var resolvedSlug = !string.IsNullOrWhiteSpace(slug) ? slug.Trim() : TryReadString(app, "publicUrl");
        var authentication = requiresAuthentication ?? TryReadBool(app, "requiresAuthentication") ?? false;
        var forkable = isForkable ?? TryReadBool(app, "isForkable") ?? false;
        if (string.IsNullOrWhiteSpace(resolvedSlug))
        {
            if (yes || nonInteractive)
            {
                checks.Add(new CommandDiagnosticCheck(
                    CommandDiagnosticCheckNames.Publish, false, "public URL slug is required for first publish"));
                nextSteps.Add("Rerun automaton publish --slug <your-app-slug>");
                return new PublishSettingsResult(null, CreatePublishSettingsFailure(
                    context, checks, nextSteps, authentication, forkable));
            }
            resolvedSlug = PromptForSlug(context.AppConfig.Name);
        }
        return new PublishSettingsResult(new PublishSettings(resolvedSlug!, authentication, forkable), null);
    }

    private static PublishResult CreatePublishSettingsFailure(
        PublishContext context,
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        bool requiresAuthentication,
        bool isForkable)
        => new()
        {
            Success = false,
            Summary = "Publish failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            AppId = context.AppId,
            AppName = context.AppConfig.Name,
            StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(context.Server, context.AppId),
            RequiresAuthentication = requiresAuthentication,
            IsForkable = isForkable
        };

    private static PublishResult? ResolvePublishApproval(
        PublishContext context,
        PublishSettings settings,
        bool yes,
        bool nonInteractive,
        ICollection<CommandDiagnosticCheck> checks)
    {
        if (yes) return null;
        var publicUrl = CliAppUrlBuilder.BuildPublishedUrl(context.Server, settings.Slug);
        if (nonInteractive)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.PublishApproval, false,
                "publish requires --yes in non-interactive mode"));
            return CreateApprovalFailure(context, settings, checks,
                ["Rerun automaton publish --yes once you intend to make the app public"]);
        }
        Console.Write($"Publish '{context.AppConfig.Name}' at {publicUrl}? [y/N]: ");
        var confirmation = Console.ReadLine()?.Trim();
        if (string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase)) return null;
        checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.PublishApproval, false, "publish cancelled by user"));
        return CreateApprovalFailure(context, settings, checks,
            ["Rerun automaton publish --yes when you are ready to make the app public"], cancelled: true);
    }

    private static PublishResult CreateApprovalFailure(
        PublishContext context,
        PublishSettings settings,
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        bool cancelled = false)
        => new()
        {
            Success = false,
            Cancelled = cancelled,
            Summary = cancelled ? "Publish cancelled" : "Publish failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            AppId = context.AppId,
            AppName = context.AppConfig.Name,
            Slug = settings.Slug,
            Url = CliAppUrlBuilder.BuildPublishedUrl(context.Server, settings.Slug),
            StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(context.Server, context.AppId),
            RequiresAuthentication = settings.RequiresAuthentication,
            IsForkable = settings.IsForkable
        };

    private static PublishResult CreatePublishSuccess(
        PublishContext context,
        PublishSettings settings,
        IEnumerable<CommandDiagnosticCheck> checks)
    {
        var publishedUrl = CliAppUrlBuilder.BuildPublishedUrl(context.Server, settings.Slug);
        var resultChecks = checks.ToList();
        resultChecks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.Publish, true, $"published at {publishedUrl}"));
        return new PublishResult
        {
            Success = true,
            Summary = "Publish complete",
            Checks = resultChecks,
            NextSteps = [],
            AppId = context.AppId,
            AppName = context.AppConfig.Name,
            Slug = settings.Slug,
            Url = publishedUrl,
            StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(context.Server, context.AppId),
            RequiresAuthentication = settings.RequiresAuthentication,
            IsForkable = settings.IsForkable
        };
    }

    private sealed record PublishContext(
        AutomatonConfig AppConfig, string Server, string ApiKey, string AppId);
    private sealed record PublishContextResolution(PublishContext? Context, PublishResult? Failure);
    private sealed record PublishSettings(string Slug, bool RequiresAuthentication, bool IsForkable);
    private sealed record PublishSettingsResult(PublishSettings? Settings, PublishResult? Failure);
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
