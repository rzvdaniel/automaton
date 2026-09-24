using System.Text;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class AppActionExecutionInputValidator
{
    private const int MaximumInputBytes = 1_048_576;

    public static string? Validate(JsonElement input, string serializedInput, Dictionary<string, object?>? schema)
    {
        if (Encoding.UTF8.GetByteCount(serializedInput) > MaximumInputBytes)
            return "Action input exceeds the 1 MB limit.";

        using var nullDocument = input.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("null") : null;
        var inputElement = input.ValueKind == JsonValueKind.Undefined ? nullDocument!.RootElement : input;
        return AppActionSchemaValidator.Validate(inputElement, schema);
    }
}
