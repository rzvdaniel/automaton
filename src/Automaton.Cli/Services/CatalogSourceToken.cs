using System.Text;

namespace Automaton.Cli.Services;

/// <summary>
/// Where the read token of a private catalog source comes from: a file, standard input or a hidden prompt, never an
/// argument or the environment, so it stays out of shell history, process lists and AI chat transcripts.
/// </summary>
public static class CatalogSourceToken
{
    /// <summary>Returns the token, or null for a public source. Throws a message that names the exact command when an agent has no way to ask.</summary>
    public static string? Read(string? tokenFile, bool fromStdin, bool isPrivate, string commandHint,
        TextReader? stdin = null, bool? inputRedirected = null, Func<string>? prompt = null)
    {
        if (tokenFile is not null && fromStdin) throw new InvalidOperationException("Use either --token-file or --token-stdin, not both.");
        var token = tokenFile is not null ? ReadFile(tokenFile)
            : fromStdin ? (stdin ?? Console.In).ReadToEnd()
            : isPrivate ? Ask(commandHint, prompt, inputRedirected)
            : null;
        return token is null ? null : Validate(token);
    }

    private static string ReadFile(string tokenFile)
    {
        var path = tokenFile.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), tokenFile[2..])
            : tokenFile;
        return File.Exists(path) ? File.ReadAllText(path) : throw new InvalidOperationException($"The token file {tokenFile} does not exist.");
    }

    private static string Ask(string commandHint, Func<string>? prompt, bool? inputRedirected)
    {
        if (prompt is null && (inputRedirected ?? Console.IsInputRedirected)) throw new InvalidOperationException(PrivateNeedsPerson(commandHint));
        return (prompt ?? PromptHidden)();
    }

    private static string Validate(string token)
    {
        token = token.Trim();
        if (token.Length == 0) throw new InvalidOperationException("The token is empty.");
        if (token.Any(char.IsWhiteSpace)) throw new InvalidOperationException("The token must be one line without spaces.");
        return token;
    }

    /// <summary>What an agent prints instead of guessing: the person runs this command and types or points to the token themselves.</summary>
    public static string PrivateNeedsPerson(string commandHint) =>
        "This looks like a private repository and the token must come from you, not from an AI assistant.\n" +
        "Create a read-only token: GitHub, Settings, Developer settings, Fine-grained tokens, Generate new token; choose Only select repositories and pick this one; Repository permissions, Contents: Read-only.\n" +
        $"Save it to a file only you can read (for example ~/catalog-token, chmod 600), then run this yourself in a terminal:\n  {commandHint}\n" +
        "(without --token-file you are asked for the token and nothing is echoed). Nothing was added.";

    private static string PromptHidden()
    {
        Console.Error.Write("Read token (hidden): ");
        var text = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return text.ToString();
    }
}
