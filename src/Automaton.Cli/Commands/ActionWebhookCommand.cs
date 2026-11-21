using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

/// <summary><c>automaton action webhook</c>: manage the addresses that start an Action from outside Automaton.</summary>
public static class ActionWebhookCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("webhook", "Start an Action from outside through a webhook with a secret");
        command.AddCommand(BuildAdd(config));
        command.AddCommand(BuildList(config));
        command.AddCommand(BuildRotate(config));
        command.AddCommand(BuildRemove(config));
        return command;
    }

    private static Option<string?> NameOption() => new("--name", "The webhook's name; needed when the Action has more than one");

    private static Command BuildAdd(ConfigService config)
    {
        var key = new Argument<string>("action-key");
        var name = new Option<string?>("--name", "A name for the webhook (default: the Action key)");
        var command = new Command("add", "Add a webhook to an Action and print its secret once") { key, name };
        command.SetHandler(async (string actionKey, string? hookName) =>
        {
            var (server, apiKey, appId) = ActionCommand.Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            await Guard(async () =>
                PrintSecret(await client.CreateActionWebhookAsync(appId, actionKey, string.IsNullOrWhiteSpace(hookName) ? actionKey : hookName)));
        }, key, name);
        return command;
    }

    private static Command BuildList(ConfigService config)
    {
        var key = new Argument<string?>("action-key", () => null, "Only this Action's webhooks");
        var command = new Command("list", "List webhooks and their last calls") { key };
        command.SetHandler(async (string? actionKey) =>
        {
            var (server, apiKey, appId) = ActionCommand.Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            await Guard(async () =>
            {
                foreach (var hook in (await client.ListActionWebhooksAsync(appId)).EnumerateArray()
                             .Where(item => actionKey is null || item.GetProperty("actionKey").GetString() == actionKey))
                    Console.WriteLine(Describe(hook));
            });
        }, key);
        return command;
    }

    private static Command BuildRotate(ConfigService config)
    {
        var key = new Argument<string>("action-key");
        var name = NameOption();
        var command = new Command("rotate", "Replace a webhook's secret; the old one stops working at once") { key, name };
        command.SetHandler(async (string actionKey, string? hookName) =>
        {
            var (server, apiKey, appId) = ActionCommand.Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            await Guard(async () =>
            {
                var hook = await Find(client, appId, actionKey, hookName);
                PrintSecret(await client.RotateActionWebhookAsync(appId, hook.GetProperty("id").GetGuid()));
            });
        }, key, name);
        return command;
    }

    private static Command BuildRemove(ConfigService config)
    {
        var key = new Argument<string>("action-key");
        var name = NameOption();
        var command = new Command("remove", "Remove a webhook") { key, name };
        command.SetHandler(async (string actionKey, string? hookName) =>
        {
            var (server, apiKey, appId) = ActionCommand.Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            await Guard(async () =>
            {
                var hook = await Find(client, appId, actionKey, hookName);
                await client.DeleteActionWebhookAsync(appId, hook.GetProperty("id").GetGuid());
                Console.WriteLine($"Removed webhook {hook.GetProperty("name").GetString()}.");
            });
        }, key, name);
        return command;
    }

    internal static async Task<JsonElement> Find(AutomatonApiClient client, string appId, string actionKey, string? name)
    {
        var all = await client.ListActionWebhooksAsync(appId);
        return Pick(all, actionKey, name);
    }

    internal static JsonElement Pick(JsonElement webhooks, string actionKey, string? name)
    {
        var matches = webhooks.EnumerateArray()
            .Where(item => item.GetProperty("actionKey").GetString() == actionKey
                && (string.IsNullOrEmpty(name) || string.Equals(item.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matches.Count == 1) return matches[0];
        throw new InvalidOperationException(matches.Count == 0
            ? $"Action '{actionKey}' has no matching webhook."
            : $"Action '{actionKey}' has {matches.Count} webhooks; choose one with --name.");
    }

    internal static string Describe(JsonElement hook)
    {
        var state = hook.GetProperty("enabled").GetBoolean() ? "on" : "off";
        var calls = hook.GetProperty("calls");
        var last = calls.GetArrayLength() > 0
            ? $"last call {calls[0].GetProperty("outcome").GetString()} {calls[0].GetProperty("calledAtUtc").GetString()}"
            : "not called yet";
        return $"{hook.GetProperty("actionKey").GetString()}\t{hook.GetProperty("name").GetString()}\t{state}\t{hook.GetProperty("url").GetString()}\t{last}";
    }

    private static void PrintSecret(JsonElement hook)
    {
        Console.WriteLine($"Webhook {hook.GetProperty("name").GetString()}: POST {hook.GetProperty("url").GetString()}");
        Console.WriteLine($"Secret (shown once): {hook.GetProperty("secret").GetString()}");
        Console.WriteLine("Send it as: Authorization: Bearer <secret>. The request's JSON body becomes the Action's input.");
    }

    private static async Task<bool> Guard(Func<Task> work)
    {
        try
        {
            await work();
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error: {ActionCommand.ApiErrorText(exception.Message)}");
            Environment.ExitCode = 1;
            return false;
        }
    }
}
