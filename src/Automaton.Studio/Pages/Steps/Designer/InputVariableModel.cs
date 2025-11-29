using Automaton.Core.Enums;

namespace Automaton.Studio.Pages.Steps.Designer;

public class InputVariableModel
{
    public string Name { get; set; }
    public VariableType Type { get; set; }
    public string Description { get; set; }
    public IEnumerable<VariableType> Types { get; } = Enum.GetValues<VariableType>();
    
    /// <summary>
    /// Indicates if this variable contains sensitive data (passwords, API keys, etc.)
    /// </summary>
    public bool IsSensitive { get; set; } = false;
}
