namespace Automaton.Api.Entities;

public class CustomStep
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MoreInfo { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool VisibleInExplorer { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    public virtual ICollection<CustomStepUser> CustomStepUsers { get; set; } = new List<CustomStepUser>();
}
