namespace Automaton.Studio.Server.Entities;

public class WebAppScreenshot
{
    public Guid Id { get; set; }
    public Guid WebAppId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual WebApp? WebApp { get; set; }
}
