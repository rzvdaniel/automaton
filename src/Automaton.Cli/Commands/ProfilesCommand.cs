using System.CommandLine;
using System.Security.Cryptography;
using System.Text;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

public static class ProfilesCommand
{
    public static Command Build(ConfigService config)
    {
        var json = new Option<bool>("--json", "Print profiles as JSON");
        var command = new Command("profiles", "List and select server profiles") { json };
        command.SetHandler((bool asJson) => List(config, asJson), json);
        var name = new Argument<string>("name", "Profile to use as the default");
        var use = new Command("use", "Select the current profile") { name };
        use.SetHandler((string selected) =>
        {
            try
            {
                config.UseProfile(selected);
                Console.Out.WriteLine($"Current profile: {selected}");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                Environment.ExitCode = 1;
            }
        }, name);
        command.AddCommand(use);
        return command;
    }

    private static void List(ConfigService config, bool asJson)
    {
        var profiles = config.LoadProfilesConfig();
        var entries = profiles.Profiles.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new
            {
                name = pair.Key,
                server = pair.Value.Server,
                current = string.Equals(pair.Key, profiles.Current, StringComparison.OrdinalIgnoreCase),
                keyFingerprint = Fingerprint(pair.Value.ApiKey)
            }).ToArray();
        if (asJson) { CliJson.Write(new { profiles = entries }); return; }
        var table = new Table().AddColumns("Name", "Server", "Current", "Key fingerprint");
        foreach (var entry in entries)
            table.AddRow(Markup.Escape(entry.name), Markup.Escape(entry.server ?? ""), entry.current ? "yes" : "", entry.keyFingerprint);
        AnsiConsole.Write(table);
    }

    private static string Fingerprint(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "-";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(digest.AsSpan(0, 6)).ToLowerInvariant();
    }
}
