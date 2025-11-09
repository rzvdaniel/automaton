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
    /// JSON-serialized script engine configuration settings
    /// Stores RunnerScriptEngineSettings as JSON string
    /// </summary>
    public string? ScriptEngineSettings { get; set; }
    
    // Project/Organization foreign key
    public Guid? ProjectId { get; set; }

    public virtual IEnumerable<RunnerUser> RunnerUsers { get; set; }
    public virtual Project Project { get; set; }
}
