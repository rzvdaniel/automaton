using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Cli.Services;

public static class CliJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Write(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, Options));
    }
}