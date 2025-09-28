using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

public enum MessageRole
{
    User = 1,
    Assistant = 2,
    System = 3
}

public class AiConversationMessage
{
    public Guid Id { get; set; }
    
    [Required]
    public Guid ConversationId { get; set; }
    
    [Required]
    public MessageRole Role { get; set; }
    
    [Required]
    public string Content { get; set; } = string.Empty;
    
    public string? GeneratedCode { get; set; }
    
    public bool HasError { get; set; } = false;
    
    public string? ErrorMessage { get; set; }
    
    public AiProvider? Provider { get; set; }
    
    [MaxLength(100)]
    public string? Model { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public AiConversation Conversation { get; set; } = default!;
}