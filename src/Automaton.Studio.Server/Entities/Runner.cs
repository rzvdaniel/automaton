#nullable disable

using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

public class Runner
{
    [Required]
    public Guid Id { get; set; }
    [Required]
    public string Name { get; set; }
    public string ConnectionId { get; set; }

    /// <summary>
    /// Generic configuration property storing all runner settings as JSON
    /// Structure: { "Python": { "PythonDllPath": "...", "PythonPaths": [...], "AutoDetectPythonPaths": true } }
    /// </summary>
    public string Configuration { get; set; }

    public virtual IEnumerable<RunnerUser> RunnerUsers { get; set; }
}
