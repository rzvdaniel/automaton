using System.CommandLine;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>API keys of the signed-in user. The key is shown once; <c>--out</c> writes it to a private file instead.</summary>
public static class KeysCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("keys", "Create API keys, for example for an MCP client");
        var name = new Option<string>("--name", "Name of the key") { IsRequired = true };
        var scope = new Option<string>("--scope", () => "developer", "developer or runner");
        var output = new Option<string?>("--out", "Write the key to this file (owner-only) instead of printing it");
        var email = new Option<string>("--email", "Your email; the password is read from standard input (a saved key cannot create keys)") { IsRequired = true };
        var json = new Option<bool>("--json", "Print the result as JSON");
        var create = new Command("create", "Create an API key; the key is shown once") { name, email, scope, output, json };
        create.SetHandler((string keyName, string mail, string keyScope, string? path, bool asJson) =>
            CreateAsync(config, keyName, mail, keyScope, path, asJson), name, email, scope, output, json);
        command.AddCommand(create);
        return command;
    }

    internal static int ParseScope(string scope) => scope.Trim().ToLowerInvariant() switch
    {
        "developer" => ApiKeyScopes.Developer,
        "runner" => ApiKeyScopes.Runner,
        _ => throw new InvalidOperationException("Scope must be developer or runner.")
    };

    private static async Task CreateAsync(ConfigService config, string name, string email, string scope, string? path, bool asJson)
    {
        try
        {
            var value = ParseScope(scope);
            var context = config.ResolveContext(directory: Directory.GetCurrentDirectory(), environmentName: null);
            if (string.IsNullOrWhiteSpace(context.Server))
                throw new InvalidOperationException("No server is configured. Run automaton login first.");
            if (!Console.IsInputRedirected)
                throw new InvalidOperationException("Pipe the password on standard input, for example: automaton keys create --name mcp --email E < password-file");
            var password = Console.In.ReadToEnd().TrimEnd('\r', '\n');
            if (password.Length == 0) throw new InvalidOperationException("No password was read from standard input.");
            using var client = new AutomatonApiClient(context.Server);
            var token = await client.SignInWithPasswordAsync(email.Trim(), password);
            var created = await client.CreateApiKeyWithAccessTokenAsync(token, name, value);
            var key = created.GetProperty("apiKey").GetString()
                ?? throw new InvalidOperationException("The server did not return an API key.");
            if (!string.IsNullOrWhiteSpace(path))
            {
                WritePrivateFile(path, key);
                if (asJson) CliJson.Write(new { success = true, name, scope, savedTo = path });
                else Console.WriteLine($"Key '{name}' saved to {path}");
            }
            else if (asJson) CliJson.Write(new { success = true, name, scope, apiKey = key });
            else Console.WriteLine(key);
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            Environment.ExitCode = 1;
        }
    }

    internal static void WritePrivateFile(string path, string content)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, "");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.WriteAllText(path, content + Environment.NewLine, new System.Text.UTF8Encoding(false));
    }
}
