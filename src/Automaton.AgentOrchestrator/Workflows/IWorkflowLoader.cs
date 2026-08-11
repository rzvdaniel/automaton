namespace Automaton.AgentOrchestrator.Workflows;

public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadAsync(string path, CancellationToken cancellationToken);
}
