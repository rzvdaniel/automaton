namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Stores encrypted sensitive variable values separately from workflows/custom steps.
/// Variables marked as sensitive are stored here and referenced by GUID in the workflow JSON.
/// </summary>
public class SensitiveVariable
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Display name for the variable (for user identification)
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// Encrypted variable value using AES-256
    /// </summary>
    public string EncryptedValue { get; set; }
    
    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Type of resource this variable belongs to (Flow, CustomStep)
    /// </summary>
    public string ResourceType { get; set; }
    
    /// <summary>
    /// ID of the parent resource (FlowId or CustomStepId)
    /// </summary>
    public Guid ResourceId { get; set; }
    
    /// <summary>
    /// Variable scope: Input or Output
    /// </summary>
    public string VariableScope { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Last time this variable was accessed/used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public virtual ICollection<SensitiveVariableUser> SensitiveVariableUsers { get; set; }
}
