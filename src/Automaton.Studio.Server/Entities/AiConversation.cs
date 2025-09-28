using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

public class AiConversation
{
    public Guid Id { get; set; }
    
    [Required]
    public Guid UserId { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Language { get; set; } = "Python";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ApplicationUser User { get; set; } = default!;
    public ICollection<AiConversationMessage> Messages { get; set; } = new List<AiConversationMessage>();
}