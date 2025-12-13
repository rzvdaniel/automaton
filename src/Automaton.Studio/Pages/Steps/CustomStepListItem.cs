using Automaton.Studio.Enums;

namespace Automaton.Studio.Pages.Steps;

public class CustomStepListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    /// <summary>
    /// The WebApp ID this step is associated with (for AppGenerated steps)
    /// </summary>
    public Guid? WebAppId { get; set; }

    /// <summary>
    /// The WebApp name this step is associated with (for display purposes)
    /// </summary>
    public string? WebAppName { get; set; }
}
