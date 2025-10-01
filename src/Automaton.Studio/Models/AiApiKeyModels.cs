namespace Automaton.Studio.Models;

public class AiApiKeyModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AiProvider Provider { get; set; }
    public string? Model { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
}

public class AiProviderInfo
{
    public AiProvider Provider { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public bool HasStoredKeys { get; set; }
    public int StoredKeyCount { get; set; }
    public bool HasEnvironmentKey { get; set; }
    public List<AiApiKeyModel> StoredKeys { get; set; } = new();
    public List<string> SupportedModels { get; set; } = new();
}

public class CreateAiApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public AiProvider Provider { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Model { get; set; }
}

public class CodeGenerationRequest
{
    public string Description { get; set; } = string.Empty;
    public string Language { get; set; } = "Python";
    public AiProvider Provider { get; set; }
    public Guid? SelectedAiApiKeyId { get; set; }
    public string? Model { get; set; }
    public List<VariableInfo>? InputVariables { get; set; }
    public List<VariableInfo>? OutputVariables { get; set; }
    public List<ConversationMessage>? ConversationHistory { get; set; }
    public bool EnableTools { get; set; } = true; // Enable MCP tools by default
    public string? CurrentStepId { get; set; } // For context when using tools
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
    public List<McpToolCall>? ToolCalls { get; set; }
    public List<McpToolResult>? ToolResults { get; set; }
}

// MCP Tool Models
public class McpToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

public class McpToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum AiProvider
{
    OpenAI = 1,
    Anthropic = 2,
    Google = 3,
    Azure = 4,
    Custom = 99
}