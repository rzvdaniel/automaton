namespace Automaton.Studio.Models;

public class AiApiKeyModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
}

public class AiProviderInfo
{
    /// <summary>
    /// Provider identifier name (e.g., "OpenAI", "Anthropic")
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Display name for UI (e.g., "OpenAI", "Anthropic Claude")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description of the provider
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Icon name for UI (e.g., "openai", "anthropic", "google")
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// URL where users can get their API key
    /// </summary>
    public string? ApiKeyHelpUrl { get; set; }

    /// <summary>
    /// Whether there is a registered implementation for this provider
    /// </summary>
    public bool IsImplemented { get; set; }

    public bool HasStoredKeys { get; set; }
    public int StoredKeyCount { get; set; }
    public bool HasEnvironmentKey { get; set; }
    public List<AiApiKeyModel> StoredKeys { get; set; } = new();
    public List<string> SupportedModels { get; set; } = new();
}

public class CreateAiApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CodeGenerationRequest
{
    public string Description { get; set; } = string.Empty;
    public string Language { get; set; } = "Python";
    public string Provider { get; set; } = string.Empty;
    public Guid? SelectedAiApiKeyId { get; set; }
    public string? Model { get; set; }
    public List<VariableInfo>? InputVariables { get; set; }
    public List<VariableInfo>? OutputVariables { get; set; }
    public List<ConversationMessage>? ConversationHistory { get; set; }
}

public class VariableInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CodeGenerationResponse
{
    public bool IsSuccess { get; set; }
    public string GeneratedCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}