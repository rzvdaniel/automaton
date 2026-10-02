namespace Automaton.Api.Config;

public sealed class DeploymentDispatchOptions
{
    public const string SectionName = "DeploymentDispatch";

    public int MaximumConcurrentOperations { get; set; } = 4;
}
