namespace Automaton.Mcp.Client.Models;

/// <summary>
/// Response from server when getting custom step
/// </summary>
public class CustomStepResponse
{
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = "Python";
}

/// <summary>
/// Request to execute a custom step
/// </summary>
public class ExecuteStepRequest
{
    public Guid StepId { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}

/// <summary>
/// Result of executing a custom step
/// </summary>
public class ExecutionResult
{
    public bool IsSuccess { get; set; }
    public string Logs { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}