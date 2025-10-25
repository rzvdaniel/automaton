namespace Automaton.Studio.Pages.Apps.Components.PublishAppDialog;

public class PublishAppModel
{
    public Guid AppId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public bool RequiresAuthentication { get; set; } = true; // Protected by default
}
