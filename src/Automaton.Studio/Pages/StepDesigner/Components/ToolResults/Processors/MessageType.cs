namespace Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Processors;

public enum MessageType
{
    Text,
    Generating,
    CodeSuccess,
    CodeError,
    System,
    ToolExecution,
    
    // New: Progressive AI operation status
    OperationInProgress,  // "🤖 Reading current code..." / "🧪 Testing code execution..."
    OperationComplete     // Shows result after operation
}

