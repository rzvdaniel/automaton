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
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            var name = equals > 0 ? line[..equals].Trim() : "";
            if (name.Length == 0 || name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                throw new InvalidOperationException($"{origin}, line {number}: expected NAME=VALUE (letters, digits and _ in the name).");
            values[name] = line[(equals + 1)..];
        }
        return values;
    }

    public static Dictionary<string, string> WithoutEmpty(Dictionary<string, string> values)
        => values.Where(item => item.Value.Trim().Length > 0).ToDictionary(item => item.Key, item => item.Value);

    public static Dictionary<string, string> ParseSettings(IEnumerable<string> arguments)
        => ParseLines(string.Join('\n', arguments), "--setting");

    /// <summary>Secrets from a file or standard input; the two are alternatives. Returns none when neither is given.</summary>
    public static Dictionary<string, string> ReadSecrets(string? file, bool fromStdin, TextReader? stdin = null)
    {
        if (file is not null && fromStdin) throw new InvalidOperationException("Use either --secrets-file or --secrets-stdin, not both.");
        if (file is not null)
        {
            var path = file.StartsWith("~/", StringComparison.Ordinal)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), file[2..]) : file;
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
        var missingSecrets = declaredSecrets.Where(item => string.IsNullOrEmpty(item.Value.GetTextOrEmpty("from")) && !secrets.ContainsKey(item.Key))
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
