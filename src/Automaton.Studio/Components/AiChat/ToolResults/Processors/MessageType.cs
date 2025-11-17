namespace Automaton.Studio.Components.AiChat.ToolResults.Processors;

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

