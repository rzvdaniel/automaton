namespace Automaton.Studio.Server.Entities;

public class WebApp
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Definition { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    public virtual ICollection<WebAppUser> WebAppUsers { get; set; }
}
