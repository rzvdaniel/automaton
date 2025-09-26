using Automaton.Studio.Server.Entities;

namespace Automaton.Studio.Server.Services.AiProviders;

public interface IAiProvider
{
    AiProvider ProviderType { get; }
    string DefaultModel { get; }
    Task<string> GenerateCodeAsync(string apiKey, string prompt, string model);
}