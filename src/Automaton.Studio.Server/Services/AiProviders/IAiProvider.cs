using Automaton.Studio.Server.Entities.Enums;

namespace Automaton.Studio.Server.Services.AiProviders;

public record ConversationMessage(string Role, string Content);

public interface IAiProvider
{
    AiProviderType ProviderType { get; }
    string DefaultModel { get; }
    IReadOnlyList<string> SupportedModels { get; }
}