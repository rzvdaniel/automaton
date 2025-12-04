using Automaton.Core.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json.Linq;

namespace Automaton.Studio.Steps.Custom.Variables;

public partial class OutputVariable
{
    [Parameter]
    public StepVariable Variable { get; set; }

    private bool isValueModalVisible = false;

    private void ShowValueModal()
    {
        isValueModalVisible = true;
    }

    private bool IsJsonValue()
    {
        if (Variable?.Value == null) return false;
        var valueStr = Variable.Value.ToString();
        if (string.IsNullOrWhiteSpace(valueStr)) return false;

        try
        {
            JToken.Parse(valueStr);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetTypeBadge()
    {
        if (!IsJsonValue()) return "TEXT";

        try
        {
            var valueStr = Variable.Value?.ToString() ?? "";
            var obj = JToken.Parse(valueStr);
            return obj.Type switch
            {
                JTokenType.Array => "ARRAY",
                JTokenType.Object => "OBJECT",
                _ => "JSON"
            };
        }
        catch
        {
            return "TEXT";
        }
    }

    private string GetJsonIcon()
    {
        try
        {
            var valueStr = Variable.Value?.ToString() ?? "";
            var obj = JToken.Parse(valueStr);
            return obj.Type switch
            {
                JTokenType.Array => "[ ]",
                JTokenType.Object => "{ }",
                _ => "{ }"
            };
        }
        catch
        {
            return "";
        }
    }

    private string GetJsonSummary()
    {
        try
        {
            var valueStr = Variable.Value?.ToString() ?? "";
            var obj = JToken.Parse(valueStr);

            return obj.Type switch
            {
                JTokenType.Array => $"{((JArray)obj).Count} items",
                JTokenType.Object => $"{((JObject)obj).Count} properties",
                _ => "JSON data"
            };
        }
        catch
        {
            return "";
        }
    }

    private string GetValuePreview()
    {
        if (Variable?.Value == null) return "(empty)";
        var valueStr = Variable.Value.ToString() ?? "";
        return valueStr.Length > 80 ? valueStr.Substring(0, 80) + "..." : valueStr;
    }

    private int GetValueSize()
    {
        return Variable?.Value?.ToString()?.Length ?? 0;
    }

    private string GetFormattedValue()
    {
        if (Variable?.Value == null) return "(empty)";

        var valueStr = Variable.Value.ToString();
        if (string.IsNullOrEmpty(valueStr)) return "(empty)";

        try
        {
            var obj = JToken.Parse(valueStr);
            return obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        catch
        {
            return valueStr;
        }
    }

    private void CopyToClipboard()
    {
        // Placeholder for clipboard functionality
        // You can implement with JS Interop if needed
    }
}
