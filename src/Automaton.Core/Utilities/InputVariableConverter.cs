using Newtonsoft.Json;

namespace Automaton.Core.Utilities;

/// <summary>
/// Utility class for converting input variables to string format for script execution.
/// ✅ IMPORTANT: If value is already a string, it's returned as-is (not JSON serialized).
/// This prevents double-encoding which would add quotes around the string.
/// For example: "api-key" should stay "api-key", not become "\"api-key\"" (with escaped quotes).
/// </summary>
public static class InputVariableConverter
{
    /// <summary>
    /// Convert an input value to string format for script execution.
    /// - Strings: Returned as-is (no JSON encoding)
    /// - Other types: Serialized to JSON
    /// - Null: Returns empty string
    /// </summary>
    public static string ConvertToString(object? value)
    {
        if (value == null)
            return string.Empty;

        // ✅ If it's already a string, return it as-is (don't serialize again)
        if (value is string stringValue)
            return stringValue;

        // For all other types, serialize to JSON
        return JsonConvert.SerializeObject(value);
    }
}
