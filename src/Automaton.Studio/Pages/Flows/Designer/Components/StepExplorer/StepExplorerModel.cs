namespace Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;

public class StepExplorerModel
{
    private const string DefaultIcon = "file";
    
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Type { get; set; }
    public string Description { get; set; }
    public string Icon { get; set; } = DefaultIcon;
    public string MoreInfo { get; set; } = string.Empty;
    public string Category { get; set; }
    public bool VisibleInExplorer { get; set; }
    public bool IsCustomStep { get; set; }
    public string Title => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;

    public List<StepExplorerModel> Steps { get; set; } = [];

    public StepExplorerModel()
    {
        Name = "Step";
        Type = "Step";
    }

    public StepExplorerModel(string name, string displayName)
    {
        Name = name;
        DisplayName = displayName;
    }

    public void AddStep(StepExplorerModel step)
    {
        Steps.Add(step);  
    }

    public bool IsCategory()
    {
        return Steps != null && Steps.Any();
    }
}
