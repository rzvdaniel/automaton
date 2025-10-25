namespace Automaton.Studio.Server.Models;

public class AppModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    public AppDefinition Definition { get; set; } = new AppDefinition();
}
