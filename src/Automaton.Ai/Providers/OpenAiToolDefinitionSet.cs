using Automaton.Ai.Models;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class OpenAiToolDefinitionSet : IDisposable
{
    private readonly List<JsonDocument> _schemaDocuments;

    private OpenAiToolDefinitionSet(
        List<JsonDocument> schemaDocuments,
        List<Dictionary<string, object?>> definitions)
    {
        _schemaDocuments = schemaDocuments;
        Definitions = definitions;
    }

    public List<Dictionary<string, object?>> Definitions { get; }

    public static OpenAiToolDefinitionSet Create(IEnumerable<ProviderTool> tools)
    {
        var documents = new List<JsonDocument>();
        try
        {
            var definitions = tools.Select(tool =>
            {
                var schema = JsonDocument.Parse(tool.InputSchemaJson);
                documents.Add(schema);
                return new Dictionary<string, object?>
                {
                    ["type"] = "function",
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = schema.RootElement
                };
            }).ToList();
            return new OpenAiToolDefinitionSet(documents, definitions);
        }
        catch
        {
            foreach (var document in documents)
                document.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var document in _schemaDocuments)
            document.Dispose();
    }
}
