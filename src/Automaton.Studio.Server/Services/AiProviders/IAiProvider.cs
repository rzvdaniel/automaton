using Automaton.Studio.Server.Entities;

namespace Automaton.Studio.Server.Services.AiProviders;

public record ConversationMessage(string Role, string Content);

public interface IAiProvider
{
    AiProvider ProviderType { get; }
    string DefaultModel { get; }
    IReadOnlyList<string> SupportedModels { get; }
    
    /// <summary>
    /// Generate code with conversation history context
    /// </summary>
    Task<string> GenerateCodeAsync(string apiKey, string prompt, string model, List<ConversationMessage>? conversationHistory = null);
    
    /// <summary>
    /// Legacy method for single prompt (for backward compatibility)
    /// </summary>
    Task<string> GenerateCodeAsync(string apiKey, string prompt, string model);
}