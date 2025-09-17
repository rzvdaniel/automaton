using Automaton.Core.Enums;

namespace Automaton.Core.Models;

public class WorkflowExecution : IDisposable
{
    private bool hasErrors;
    private readonly List<ExecutionLogEntry> logs = new();

    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public WorkflowStatus Status { get; set; }
    public string Application { get; set; }
    public IReadOnlyList<ExecutionLogEntry> Logs => logs.AsReadOnly();

    public WorkflowExecution(Guid flowId, string application)
    {
        Id = Guid.NewGuid();
        FlowId = flowId;
        Started = DateTime.UtcNow;
        Status = WorkflowStatus.Working;
        Application = application;
    }

    public void AddLog(string level, string message, string stepId = null, string stepName = null, Exception exception = null)
    {
        logs.Add(new ExecutionLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message,
            StepId = stepId,
            StepName = stepName,
            Exception = exception?.ToString()
        });
    }

    public void HasErrors()
    {
        hasErrors = true;
    }

    public void Dispose()
    {
        Finished = DateTime.UtcNow;
        Status = hasErrors ? WorkflowStatus.Error : WorkflowStatus.Success;
    }
}

public class ExecutionLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; }
    public string Message { get; set; }
    public string StepId { get; set; }
    public string StepName { get; set; }
    public string Exception { get; set; }
}
