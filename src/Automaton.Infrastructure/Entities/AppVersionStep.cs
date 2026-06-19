using Automaton.Api.Enums;

namespace Automaton.Api.Entities;

public class AppVersionStep
{
    public Guid Id { get; set; }
    public Guid AppVersionId { get; set; }
    public Guid StepId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public AppVersionStepAction Action { get; set; }
    public string? Language { get; set; }
    public string? Description { get; set; }
    public string CodeSnapshot { get; set; } = string.Empty;
    public string VariablesSnapshot { get; set; } = string.Empty;

    public virtual AppVersion? AppVersion { get; set; }
    public virtual CustomStep? Step { get; set; }
}
