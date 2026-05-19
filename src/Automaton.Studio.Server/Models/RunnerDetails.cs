using Automaton.Core.Enums;
using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Models;

public class RunnerDetails
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? ConnectionId { get; set; }
    public RunnerStatus Status { get; set; }

    // Project context
    public AccessScope Scope { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public Guid? HostedRunnerId { get; set; }
    public bool IsHostedRunner { get; set; }
}
