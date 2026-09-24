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
            var project = ResolveProject(directory, environmentName, checks, nextSteps);
            if (project.Failure is not null) return project.Failure;
            var context = project.Context!;
            using var client = new AutomatonApiClient(context.Server, context.ApiKey);
            var app = await client.GetAppAsync(context.AppId, cancellationToken);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, true, $"remote app loaded: {context.AppId}"));

            var publication = ResolvePublication(app, context, slug, requiresAuthentication, isForkable, yes, nonInteractive, checks, nextSteps);
            if (publication.Failure is not null) return publication.Failure;
            var options = publication.Options!;
            var approvalFailure = ConfirmPublish(context, options, yes, nonInteractive, checks);
            if (approvalFailure is not null) return approvalFailure;

            await client.PublishAppAsync(context.AppId, options.Slug, options.RequiresAuthentication,
                category: null, tags: null, options.IsForkable, cancellationToken);
            return CreateSuccess(context, options, checks);
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

    private PublishPreparation ResolveProject(
        string directory,
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var appConfig = _config.TryLoadProjectConfig(directory);
        var resolved = _config.ResolveContext(appConfig, directory, environmentName);
        var appLinkCheck = CliCommandChecks.ValidateProjectLink(
            File.Exists(Path.Combine(directory, "automaton.json")), appConfig, resolved.AppId, nextSteps);
        var credentialsCheck = CliCommandChecks.ValidateCredentials(resolved.Server, resolved.ApiKey, nextSteps);
        checks.Add(appLinkCheck);
        checks.Add(credentialsCheck);
        if (appLinkCheck.Success && credentialsCheck.Success && appConfig is not null
            && !string.IsNullOrWhiteSpace(resolved.Server) && !string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            return new PublishPreparation(new PublishContext(appConfig, resolved.Server, resolved.ApiKey, resolved.AppId!), null);
        }

        var failure = new PublishResult
        {
            Success = false,
            Summary = "Publish failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            AppId = resolved.AppId,
            AppName = appConfig?.Name,
            StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(resolved.Server, resolved.AppId)
        };
        return new PublishPreparation(null, failure);
    }

    private static PublishOptionsResult ResolvePublication(
        JsonElement app,
        PublishContext context,
        string? slug,
        bool? requiresAuthentication,
        bool? isForkable,
        bool yes,
        bool nonInteractive,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var resolvedSlug = !string.IsNullOrWhiteSpace(slug) ? slug.Trim() : TryReadString(app, "publicUrl");
        var authRequired = requiresAuthentication ?? TryReadBool(app, "requiresAuthentication") ?? false;
        var forkable = isForkable ?? TryReadBool(app, "isForkable") ?? false;
        if (!string.IsNullOrWhiteSpace(resolvedSlug))
        {
            return new PublishOptionsResult(new PublishOptions(resolvedSlug, authRequired, forkable), null);
        }

        if (yes || nonInteractive)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, false, "public URL slug is required for first publish"));
            nextSteps.Add("Rerun automaton publish --slug <your-app-slug>");
            var failure = CreateFailure(context, checks, nextSteps, authRequired, forkable);
            return new PublishOptionsResult(null, failure);
        }

        return new PublishOptionsResult(new PublishOptions(PromptForSlug(context.Config.Name), authRequired, forkable), null);
    }

    private static PublishResult? ConfirmPublish(
        PublishContext context,
        PublishOptions options,
        bool yes,
        bool nonInteractive,
        ICollection<CommandDiagnosticCheck> checks)
    {
        if (yes) return null;
        if (nonInteractive)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.PublishApproval, false, "publish requires --yes in non-interactive mode"));
            return CreateApprovalFailure(context, options, checks, cancelled: false);
        }

        Console.Write($"Publish '{context.Config.Name}' at {CliAppUrlBuilder.BuildPublishedUrl(context.Server, options.Slug)}? [y/N]: ");
        var confirmation = Console.ReadLine()?.Trim();
        var confirmed = string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase);
        if (confirmed) return null;
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.PublishApproval, false, "publish cancelled by user"));
        return CreateApprovalFailure(context, options, checks, cancelled: true);
    }

    private static PublishResult CreateApprovalFailure(
        PublishContext context,
        PublishOptions options,
        ICollection<CommandDiagnosticCheck> checks,
        bool cancelled) => new()
        {
            Success = false,
            Cancelled = cancelled,
            Summary = cancelled ? "Publish cancelled" : "Publish failed",
            Checks = checks.ToList(),
            NextSteps = [cancelled
                ? "Rerun automaton publish --yes when you are ready to make the app public"
                : "Rerun automaton publish --yes once you intend to make the app public"],
            AppId = context.AppId,
            AppName = context.Config.Name,
            Slug = options.Slug,
            Url = CliAppUrlBuilder.BuildPublishedUrl(context.Server, options.Slug),
            StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(context.Server, context.AppId),
            RequiresAuthentication = options.RequiresAuthentication,
            IsForkable = options.IsForkable
        };

    private static PublishResult CreateFailure(
        PublishContext context,
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        bool requiresAuthentication,
        bool isForkable) => new()
        {
            Success = false,
            Summary = "Publish failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            AppId = context.AppId,
            AppName = context.Config.Name,
            StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(context.Server, context.AppId),
            RequiresAuthentication = requiresAuthentication,
            IsForkable = isForkable
        };

    private static PublishResult CreateSuccess(
        PublishContext context,
        PublishOptions options,
        ICollection<CommandDiagnosticCheck> checks)
    {
        var publishedUrl = CliAppUrlBuilder.BuildPublishedUrl(context.Server, options.Slug);
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, true, $"published at {publishedUrl}"));
        return new PublishResult
        {
            Success = true,
            Summary = "Publish complete",
            Checks = checks.ToList(),
            NextSteps = [],
            AppId = context.AppId,
            AppName = context.Config.Name,
            Slug = options.Slug,
            Url = publishedUrl,
            StudioAppUrl = CliAppUrlBuilder.BuildStudioAppUrl(context.Server, context.AppId),
            RequiresAuthentication = options.RequiresAuthentication,
            IsForkable = options.IsForkable
        };
    }

    private sealed record PublishContext(AutomatonConfig Config, string Server, string ApiKey, string AppId);
    private sealed record PublishOptions(string Slug, bool RequiresAuthentication, bool IsForkable);
    private sealed record PublishPreparation(PublishContext? Context, PublishResult? Failure);
    private sealed record PublishOptionsResult(PublishOptions? Options, PublishResult? Failure);

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
