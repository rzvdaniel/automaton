namespace Automaton.Api.Models;

using System.Text.Json.Serialization;

public class CustomStep
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string MoreInfo { get; set; }
    public string Icon { get; set; }
    public string Category { get; set; }
    public bool VisibleInExplorer { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    /// <summary>
    /// The WebApp ID this step is associated with (if linked to an app)
    /// </summary>
    public Guid? WebAppId { get; set; }

    /// <summary>
    /// The WebApp name this step is associated with (for display purposes)
    /// </summary>
    public string? WebAppName { get; set; }

    public CustomStepDefinition Definition { get; set; } = new CustomStepDefinition();
}

public class UpdateCustomStepRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MoreInfo { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    [JsonRequired]
    public bool VisibleInExplorer { get; set; }
    public CustomStepDefinition Definition { get; set; } = new();
}

