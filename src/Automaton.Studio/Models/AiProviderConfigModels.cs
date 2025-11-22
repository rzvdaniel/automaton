using Automaton.Mcp.Enums;

namespace Automaton.Studio.Models;

/// <summary>
/// DTO for AI Provider configuration
/// </summary>
public class AiProviderConfigModel
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public AiProviderType Provider { get; set; }
    public string? Description { get; set; }
    public string DefaultModel { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public List<AiModelConfigModel> Models { get; set; } = new();
}

/// <summary>
/// DTO for AI Model configuration
/// </summary>
public class AiModelConfigModel
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
