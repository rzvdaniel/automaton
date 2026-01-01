namespace Automaton.Studio.Models.Admin;

/// <summary>
/// Form model for creating or editing AI Providers
/// </summary>
public class AiProviderFormModel
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

    /// <summary>
    /// Creates a form model from a create request
    /// </summary>
    public static AiProviderFormModel FromCreateRequest(AdminCreateAiProviderRequest request)
    {
        return new AiProviderFormModel
        {
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            BaseUrl = request.BaseUrl,
            IsActive = request.IsActive,
            RequiresApiKey = request.RequiresApiKey,
            EnvironmentKeyName = request.EnvironmentKeyName,
            ApiKeyHelpUrl = request.ApiKeyHelpUrl,
            IconName = request.IconName,
            SortOrder = request.SortOrder
        };
    }

    /// <summary>
    /// Creates a form model from an existing provider
    /// </summary>
    public static AiProviderFormModel FromProvider(AiProviderAdminModel provider)
    {
        return new AiProviderFormModel
        {
            Name = provider.Name,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            BaseUrl = provider.BaseUrl,
            IsActive = provider.IsActive,
            RequiresApiKey = provider.RequiresApiKey,
            EnvironmentKeyName = provider.EnvironmentKeyName,
            ApiKeyHelpUrl = provider.ApiKeyHelpUrl,
            IconName = provider.IconName,
            SortOrder = provider.SortOrder
        };
    }

    /// <summary>
    /// Converts the form model to a create request
    /// </summary>
    public AdminCreateAiProviderRequest ToCreateRequest()
    {
        return new AdminCreateAiProviderRequest
        {
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            BaseUrl = BaseUrl,
            IsActive = IsActive,
            RequiresApiKey = RequiresApiKey,
            EnvironmentKeyName = EnvironmentKeyName,
            ApiKeyHelpUrl = ApiKeyHelpUrl,
            IconName = IconName,
            SortOrder = SortOrder
        };
    }

    /// <summary>
    /// Converts the form model to an update request
    /// </summary>
    public AdminUpdateAiProviderRequest ToUpdateRequest()
    {
        return new AdminUpdateAiProviderRequest
        {
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            BaseUrl = BaseUrl,
            IsActive = IsActive,
            RequiresApiKey = RequiresApiKey,
            EnvironmentKeyName = EnvironmentKeyName,
            ApiKeyHelpUrl = ApiKeyHelpUrl,
            IconName = IconName,
            SortOrder = SortOrder
        };
    }
}
