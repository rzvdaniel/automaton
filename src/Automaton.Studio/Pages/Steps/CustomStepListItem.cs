namespace Automaton.Studio.Pages.Steps;

public class CustomStepListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public int LinkedAppCount { get; set; }
    public bool IsAppOnlyStep => LinkedAppCount > 0;
}
