namespace Automaton.Studio.Models.Admin;

public class AiProviderAdminModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; }
    public bool RequiresApiKey { get; set; }
    public string? EnvironmentKeyName { get; set; }
    public string? ApiKeyHelpUrl { get; set; }
    public string? IconName { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int ModelCount { get; set; }
    public int ActiveModelCount { get; set; }
}

public class AdminCreateAiProviderRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool RequiresApiKey { get; set; } = true;
    public string? EnvironmentKeyName { get; set; }
    public string? ApiKeyHelpUrl { get; set; }
    public string? IconName { get; set; }
    public int SortOrder { get; set; } = 0;
}

public class AdminUpdateAiProviderRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; }
    public bool RequiresApiKey { get; set; }
    public string? EnvironmentKeyName { get; set; }
    public string? ApiKeyHelpUrl { get; set; }
    public string? IconName { get; set; }
    public int? SortOrder { get; set; }
}

public class AiModelAdminModel
{
    public Guid Id { get; set; }
    public Guid AiProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int MaxTokens { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsVision { get; set; }
    public double? CostPer1kTokensInput { get; set; }
    public double? CostPer1kTokensOutput { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminCreateAiModelRequest
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public int MaxTokens { get; set; } = 4096;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsTools { get; set; } = false;
    public bool SupportsVision { get; set; } = false;
    public double CostPer1kTokensInput { get; set; }
    public double CostPer1kTokensOutput { get; set; }
    public int SortOrder { get; set; } = 0;
}

public class AdminUpdateAiModelRequest
{
    public string? ModelId { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int? MaxTokens { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsVision { get; set; }
    public double CostPer1kTokensInput { get; set; }
    public double CostPer1kTokensOutput { get; set; }
    public int? SortOrder { get; set; }
}
