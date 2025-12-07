using System.Text.Json;

namespace Automaton.Studio.Services.Mcp.Helpers;

/// <summary>
/// Extension methods for safe JSON element property access in MCP tool providers
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// Safely gets a string property from a JsonElement, returning a default value if not found or not a string
    /// </summary>
    public static string GetStringProperty(this JsonElement element, string propertyName, string defaultValue = "")
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Safely gets a boolean property from a JsonElement, returning a default value if not found
    /// </summary>
    public static bool GetBoolProperty(this JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind == JsonValueKind.True || 
                   (value.ValueKind == JsonValueKind.False ? false : defaultValue);
        }
        return defaultValue;
    }

    /// <summary>
    /// Safely gets an integer property from a JsonElement, returning a default value if not found
    /// </summary>
    public static int GetIntProperty(this JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt32(out var intValue) ? intValue : defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Safely gets a double property from a JsonElement, returning a default value if not found
    /// </summary>
    public static double GetDoubleProperty(this JsonElement element, string propertyName, double defaultValue = 0.0)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out var doubleValue) ? doubleValue : defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Checks if a property exists and is not null
    /// </summary>
    public static bool HasProperty(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null;
    }
}
