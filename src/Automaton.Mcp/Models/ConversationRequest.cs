using Automaton.Mcp.Enums;
using Automaton.Mcp.Models;

namespace Automaton.Mcp.Models
{
    /// <summary>
    /// Request to start a conversation with the AI
    /// </summary>
    public class ConversationRequest
    {
        public string ConversationId { get; set; } = Guid.NewGuid().ToString();
        public string InitialMessage { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public AiProvider Provider { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public string? Model { get; set; }
        public List<ConversationMessage>? ConversationHistory { get; set; }
        public Dictionary<string, object>? Context { get; set; }
    }
}
