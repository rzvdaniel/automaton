namespace Automaton.Studio.Models.Admin;

/// <summary>
/// Form model for creating or editing AI Models
/// </summary>
public class AiModelFormModel
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

    /// <summary>
    /// Creates a form model from a create request
    /// </summary>
    public static AiModelFormModel FromCreateRequest(AdminCreateAiModelRequest request)
    {
        return new AiModelFormModel
        {
            ModelId = request.ModelId,
            DisplayName = request.DisplayName,
            Description = request.Description,
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            MaxTokens = request.MaxTokens,
            SupportsStreaming = request.SupportsStreaming,
            SupportsTools = request.SupportsTools,
            SupportsVision = request.SupportsVision,
            CostPer1kTokensInput = request.CostPer1kTokensInput,
            CostPer1kTokensOutput = request.CostPer1kTokensOutput,
            SortOrder = request.SortOrder
        };
    }

    /// <summary>
    /// Creates a form model from an existing model
    /// </summary>
    public static AiModelFormModel FromModel(AiModelAdminModel model)
    {
        return new AiModelFormModel
        {
            ModelId = model.ModelId,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            IsDefault = model.IsDefault,
            MaxTokens = model.MaxTokens,
            SupportsStreaming = model.SupportsStreaming,
            SupportsTools = model.SupportsTools,
            SupportsVision = model.SupportsVision,
            CostPer1kTokensInput = model.CostPer1kTokensInput ?? 0,
            CostPer1kTokensOutput = model.CostPer1kTokensOutput ?? 0,
            SortOrder = model.SortOrder
        };
    }

    /// <summary>
    /// Converts the form model to a create request
    /// </summary>
    public AdminCreateAiModelRequest ToCreateRequest()
    {
        return new AdminCreateAiModelRequest
        {
            ModelId = ModelId,
            DisplayName = DisplayName,
            Description = Description,
            IsActive = IsActive,
            IsDefault = IsDefault,
            MaxTokens = MaxTokens,
            SupportsStreaming = SupportsStreaming,
            SupportsTools = SupportsTools,
            SupportsVision = SupportsVision,
            CostPer1kTokensInput = CostPer1kTokensInput,
            CostPer1kTokensOutput = CostPer1kTokensOutput,
            SortOrder = SortOrder
        };
    }

    /// <summary>
    /// Converts the form model to an update request
    /// </summary>
    public AdminUpdateAiModelRequest ToUpdateRequest()
    {
        return new AdminUpdateAiModelRequest
        {
            ModelId = ModelId,
            DisplayName = DisplayName,
            Description = Description,
            IsActive = IsActive,
            IsDefault = IsDefault,
            MaxTokens = MaxTokens,
            SupportsStreaming = SupportsStreaming,
            SupportsTools = SupportsTools,
            SupportsVision = SupportsVision,
            CostPer1kTokensInput = CostPer1kTokensInput,
            CostPer1kTokensOutput = CostPer1kTokensOutput,
            SortOrder = SortOrder
        };
    }
}
