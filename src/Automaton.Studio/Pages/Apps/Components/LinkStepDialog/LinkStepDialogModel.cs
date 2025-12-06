namespace Automaton.Studio.Pages.Apps.Components.LinkStepDialog;

public class LinkStepDialogModel
{
    public List<Models.LinkedStepInfo> AvailableSteps { get; set; } = new();
    public Guid AppId { get; set; }
    public Guid? SelectedStepId { get; set; }
}
