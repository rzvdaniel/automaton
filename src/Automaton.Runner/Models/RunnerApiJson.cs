using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Runner.Console.Models;

/// <summary>Reads Api responses the way the Api writes them: camelCase, case-insensitive, enums as text.</summary>
internal static class RunnerApiJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
