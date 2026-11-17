using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

/// <summary>The same notification feed Studio Home shows: updates, expiring certificates, offline Runners, failures.</summary>
internal static class NotificationsCommand
{
    internal static Command Build(ConfigService config)
    {
        var json = new Option<bool>("--json", "Print the notifications as JSON");
        var command = new Command("notifications", "List the notifications for your account (the same feed as Studio Home)") { json };
        command.SetHandler(context => RunAsync(config, context, json, null));

        var key = new Argument<string>("key", "Notification key from the list, for example certificate-expiring");
        var acknowledge = new Command("acknowledge", "Hide a notification everywhere (critical ones stay until the cause is fixed)") { key, json };
        acknowledge.SetHandler(context => RunAsync(config, context, json, key));
        command.AddCommand(acknowledge);
        return command;
    }

    private static async Task RunAsync(ConfigService config, InvocationContext context, Option<bool> json, Argument<string>? key)
    {
        var asJson = context.ParseResult.GetValueForOption(json);
        try
        {
            var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Run automaton login to connect to your server.");
            using var client = new AutomatonApiClient(server, apiKey);
            var token = context.GetCancellationToken();
            if (key is not null)
            {
                var value = context.ParseResult.GetValueForArgument(key);
                await client.AcknowledgeNotificationAsync(value, token);
                if (asJson) CliJson.Write(new { success = true, key = value, acknowledged = true });
                else Console.WriteLine($"Acknowledged {value}.");
                return;
            }
            var list = (await client.ListNotificationsAsync(token)).GetProperty("notifications");
            if (asJson) { CliJson.Write(new { success = true, notifications = list }); return; }
            Console.WriteLine(Format(list));
        }
        catch (Exception exception)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else Console.Error.WriteLine(exception.Message);
            context.ExitCode = 1;
        }
    }

    internal static string Format(JsonElement list)
    {
        if (list.GetArrayLength() == 0) return "No notifications.";
        return string.Join(Environment.NewLine, list.EnumerateArray().Select(item =>
            $"[{item.GetProperty("severity").GetString()}] {item.GetProperty("title").GetString()} ({item.GetProperty("key").GetString()})"
            + Environment.NewLine + "    " + item.GetProperty("detail").GetString()));
    }
}
