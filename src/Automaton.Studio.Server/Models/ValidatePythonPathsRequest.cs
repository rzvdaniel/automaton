using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request to validate Python paths on runner machine
/// </summary>
public class ValidatePythonPathsRequest
{
    [Required]
    public Guid RunnerId { get; set; }
    
    public List<string> Paths { get; set; } = new();
    
    public string? PythonDllPath { get; set; }
}
