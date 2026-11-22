using System.Text;
using System.Text.Json;

namespace Automaton.Cli.Services;

/// <summary>
/// The settings and secrets of a catalog install. Settings come from --setting NAME=VALUE; secrets only from a file,
/// standard input or a hidden prompt (NAME=VALUE lines), never from an argument, so they stay out of shell history,
/// process lists and AI chat transcripts.
/// </summary>
public static class CatalogInstallValues
{
    /// <summary>Parses NAME=VALUE lines. Blank lines and lines starting with # are skipped; the value is everything after the first =, taken literally.</summary>
    public static Dictionary<string, string> ParseLines(string text, string origin)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var number = 0;
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            number++;
            var line = lines[index].TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            var block = line.IndexOf("<<", StringComparison.Ordinal);
            if (block > 0 && (equals < 0 || block < equals))
            {
                var (blockName, blockValue) = ReadBlock(lines, ref index, ref number, line, block, origin);
                values[blockName] = blockValue;
                continue;
            }
            var name = equals > 0 ? line[..equals].Trim() : "";
            if (name.Length == 0 || name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                throw new InvalidOperationException($"{origin}, line {number}: expected NAME=VALUE (letters, digits and _ in the name).");
            values[name] = line[(equals + 1)..];
        }
        return values;
    }

    /// <summary>NAME<<END, the value lines, then END alone on a line. Every value line ends with a newline, as in a shell heredoc.</summary>
    private static (string Name, string Value) ReadBlock(string[] lines, ref int index, ref int number, string line, int block, string origin)
    {
        var name = line[..block].Trim();
        var tag = line[(block + 2)..].Trim();
        if (!IsName(name) || tag.Length == 0)
            throw new InvalidOperationException($"{origin}, line {number}: expected NAME<<END (letters, digits and _ in the name, then a closing word).");
        var value = new StringBuilder();
        while (++index < lines.Length)
        {
            number++;
            var inner = lines[index].TrimEnd('\r');
            if (inner == tag) return (name, value.ToString());
            value.Append(inner).Append('\n');
        }
        throw new InvalidOperationException($"{origin}, line {number}: the block {name}<<{tag} is not closed; end it with {tag} alone on a line.");
    }

    private static bool IsName(string name) => name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static Dictionary<string, string> WithoutEmpty(Dictionary<string, string> values)
        => values.Where(item => item.Value.Trim().Length > 0).ToDictionary(item => item.Key, item => item.Value);

    public static Dictionary<string, string> ParseSettings(IEnumerable<string> arguments)
        => ParseLines(string.Join('\n', arguments), "--setting");

    /// <summary>Secrets from a file or standard input; the two are alternatives. Returns none when neither is given.</summary>
    public static Dictionary<string, string> ReadSecrets(string? file, bool fromStdin, TextReader? stdin = null, IEnumerable<string>? secretFiles = null)
    {
        var result = ReadSecretLines(file, fromStdin, stdin);
        // --secret-file NAME=PATH: the whole file is the value, as is (line breaks kept), so a multi-line key is passed without printing it.
        foreach (var argument in secretFiles ?? [])
        {
            var equals = argument.IndexOf('=');
            var name = equals > 0 ? argument[..equals].Trim() : "";
            if (name.Length == 0 || name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')) || equals == argument.Length - 1)
                throw new InvalidOperationException("--secret-file: expected NAME=PATH (letters, digits and _ in the name).");
            var path = ExpandHome(argument[(equals + 1)..]);
            result[name] = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : throw new InvalidOperationException($"The file {argument[(equals + 1)..]} for secret {name} does not exist.");
        }
        return result;
    }

    private static string ExpandHome(string file)
        => file.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), file[2..]) : file;

    private static Dictionary<string, string> ReadSecretLines(string? file, bool fromStdin, TextReader? stdin)
    {
        if (file is not null && fromStdin) throw new InvalidOperationException("Use either --secrets-file or --secrets-stdin, not both.");
        if (file is not null)
        {
            var path = ExpandHome(file);
            return File.Exists(path) ? ParseLines(File.ReadAllText(path), file) : throw new InvalidOperationException($"The secrets file {file} does not exist.");
        }
        return fromStdin ? ParseLines((stdin ?? Console.In).ReadToEnd(), "standard input") : [];
    }

    /// <summary>What the install still needs, from the app's declarations: unknown names given, required settings without a value or default, secrets without a value or vault source.</summary>
    public static (List<string> Unknown, List<string> MissingSettings, List<string> MissingSecrets) Check(
        JsonElement application, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> secrets)
    {
        var declaredSettings = Declared(application, "settings");
        var declaredSecrets = Declared(application, "secrets");
        var unknown = settings.Keys.Where(name => !declaredSettings.ContainsKey(name)).Select(name => $"setting {name}")
            .Concat(secrets.Keys.Where(name => !declaredSecrets.ContainsKey(name)).Select(name => $"secret {name}")).ToList();
        var missingSettings = declaredSettings.Where(item => item.Value.GetBoolOrFalse("required") && string.IsNullOrEmpty(item.Value.GetTextOrEmpty("default")) && !settings.ContainsKey(item.Key))
            .Select(item => item.Key).ToList();
        var missingSecrets = declaredSecrets.Where(item => string.IsNullOrEmpty(item.Value.GetTextOrEmpty("from")) && !item.Value.GetBoolOrFalse("optional") && !secrets.ContainsKey(item.Key))
            .Select(item => item.Key).ToList();
        return (unknown, missingSettings, missingSecrets);
    }

    public static IReadOnlyDictionary<string, JsonElement> Declared(JsonElement application, string property)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (application.ValueKind == JsonValueKind.Object && application.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray())
                if (item.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } key) result[key] = item;
        return result;
    }

    /// <summary>The hidden prompt for one secret in a terminal; empty input is refused by the caller.</summary>
    public static string PromptHidden(string name)
    {
        Console.Error.Write($"Value for secret {name} (hidden): ");
        var text = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return text.ToString();
    }

    private static bool GetBoolOrFalse(this JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string GetTextOrEmpty(this JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
