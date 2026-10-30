using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

public static class ListingCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command(
            "listing",
            "Set how the linked app appears in the public marketplace: summary, description, category, tags, " +
            "cover image and screenshots. Publishing lists an app by default.");
        command.AddCommand(BuildShow(config));
        command.AddCommand(BuildSet(config));
        command.AddCommand(BuildCover(config));
        command.AddCommand(BuildAddScreenshot(config));
        command.AddCommand(BuildRemoveScreenshot(config));
        return command;
    }

    private static Command BuildShow(ConfigService config)
    {
        var options = new CommonOptions(config);
        var command = new Command("show", "Show the current marketplace listing");
        options.AddTo(command);
        command.SetHandler(context => RunAsync(context, options,
            (service, directory, environment) => service.ShowAsync(directory, environment, context.GetCancellationToken())));
        return command;
    }

    private static Command BuildSet(ConfigService config)
    {
        var summaryOption = new Option<string?>("--summary", "One-line summary shown on app cards (up to 200 characters)");
        var descriptionOption = new Option<string?>("--description", "Full description shown on the app page (up to 500 characters)");
        var categoryOption = new Option<string?>("--category", "Marketplace category, for example Games");
        var tagsOption = new Option<string?>("--tags", "Comma-separated tags, for example \"game,multiplayer\"; replaces the current tags");
        var clearCategoryOption = new Option<bool>("--clear-category", "Remove the category");
        var clearTagsOption = new Option<bool>("--clear-tags", "Remove all tags");
        var options = new CommonOptions(config);
        var command = new Command("set", "Change the listing text. Options you leave out stay as they are")
        {
            summaryOption, descriptionOption, categoryOption, tagsOption, clearCategoryOption, clearTagsOption
        };
        options.AddTo(command);
        command.SetHandler(context =>
        {
            var parse = context.ParseResult;
            return RunAsync(context, options, (service, directory, environment) =>
            {
                var update = new MarketplaceListingUpdate(
                    parse.GetValueForOption(summaryOption),
                    parse.GetValueForOption(descriptionOption),
                    Clearable(parse.GetValueForOption(categoryOption), parse.GetValueForOption(clearCategoryOption), "category"),
                    Clearable(parse.GetValueForOption(tagsOption), parse.GetValueForOption(clearTagsOption), "tags")?
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                return service.SetAsync(directory, environment, update, context.GetCancellationToken());
            });
        });
        return command;
    }

    private static Command BuildCover(ConfigService config)
    {
        var imageArgument = new Argument<string>("image", "PNG, JPG, WEBP or GIF up to 2 MB; 16:9 or 16:10 works best");
        var options = new CommonOptions(config);
        var command = new Command("cover", "Upload the cover image, which replaces the current one") { imageArgument };
        options.AddTo(command);
        command.SetHandler(context =>
        {
            var image = context.ParseResult.GetValueForArgument(imageArgument);
            return RunAsync(context, options,
                (service, directory, environment) => service.SetCoverAsync(directory, environment, image, context.GetCancellationToken()));
        });
        return command;
    }

    private static Command BuildAddScreenshot(ConfigService config)
    {
        var imagesArgument = new Argument<string[]>("images", "One or more images, PNG, JPG, WEBP or GIF up to 2 MB each")
        {
            Arity = ArgumentArity.OneOrMore
        };
        var options = new CommonOptions(config);
        var command = new Command("add-screenshot", $"Add screenshots in the order given, up to {MarketplaceListingService.MaximumScreenshots} per app")
        {
            imagesArgument
        };
        options.AddTo(command);
        command.SetHandler(context =>
        {
            var images = context.ParseResult.GetValueForArgument(imagesArgument);
            return RunAsync(context, options,
                (service, directory, environment) => service.AddScreenshotsAsync(directory, environment, images, context.GetCancellationToken()));
        });
        return command;
    }

    private static Command BuildRemoveScreenshot(ConfigService config)
    {
        var idArgument = new Argument<string>("id", "Screenshot ID from 'automaton listing show'");
        var options = new CommonOptions(config);
        var command = new Command("remove-screenshot", "Remove one screenshot") { idArgument };
        options.AddTo(command);
        command.SetHandler(context =>
        {
            var id = context.ParseResult.GetValueForArgument(idArgument);
            return RunAsync(context, options,
                (service, directory, environment) => service.RemoveScreenshotAsync(directory, environment, id, context.GetCancellationToken()));
        });
        return command;
    }

    private static async Task RunAsync(
        InvocationContext context,
        CommonOptions options,
        Func<MarketplaceListingService, string, string?, Task<MarketplaceListing>> action)
    {
        var asJson = context.ParseResult.GetValueForOption(options.Json);
        try
        {
            var listing = await action(
                new MarketplaceListingService(options.Config),
                Directory.GetCurrentDirectory(),
                context.ParseResult.GetValueForOption(options.Environment));
            if (asJson) CliJson.Write(new { success = true, listing });
            else WriteListing(listing);
            context.ExitCode = 0;
        }
        catch (Exception exception)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            context.ExitCode = 1;
        }
    }

    private static void WriteListing(MarketplaceListing listing)
    {
        AnsiConsole.MarkupLine($"[bold]Marketplace listing for {Markup.Escape(listing.Name)}[/]");
        var table = new Table().HideHeaders().AddColumn("Field").AddColumn("Value");
        table.AddRow("Summary", Markup.Escape(Or(listing.Summary)));
        table.AddRow("Description", Markup.Escape(Or(listing.Description)));
        table.AddRow("Category", Markup.Escape(Or(listing.Category)));
        table.AddRow("Tags", Markup.Escape(listing.Tags.Count == 0 ? "(none)" : string.Join(", ", listing.Tags)));
        table.AddRow("Cover", Markup.Escape(Or(listing.CoverUrl)));
        table.AddRow("Screenshots", Markup.Escape(listing.Screenshots.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, listing.Screenshots.Select(shot => shot.Id))));
        AnsiConsole.Write(table);
    }

    // An empty value tells the API to clear the field.
    private static string? Clearable(string? value, bool clear, string field)
    {
        if (clear && value is not null)
        {
            throw new InvalidOperationException($"Use either --{field} or --clear-{field}, not both.");
        }
        return clear ? "" : value;
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;

    private sealed class CommonOptions(ConfigService config)
    {
        public Option<bool> Json { get; } = new("--json", "Print the result as JSON");
        public Option<string?> Environment { get; } = new("--env", "Use a named project environment");
        public ConfigService Config { get; } = config;

        public void AddTo(Command command)
        {
            command.AddOption(Json);
            command.AddOption(Environment);
        }
    }
}
