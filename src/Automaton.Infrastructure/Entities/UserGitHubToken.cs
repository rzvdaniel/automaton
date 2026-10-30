namespace Automaton.Api.Entities;

/// <summary>
/// Stores encrypted GitHub Personal Access Tokens per user
/// </summary>
public class UserGitHubToken
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// User ID (from AspNetUsers)
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Encrypted GitHub Personal Access Token
    /// </summary>
    public string EncryptedToken { get; set; } = string.Empty;
    
    /// <summary>
    /// GitHub username associated with this token
    /// </summary>
    public string? GitHubUsername { get; set; }
    
    /// <summary>
    /// When the token was added
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When the token was last validated
    /// </summary>
    public DateTime? LastValidatedAt { get; set; }
    
    /// <summary>
    /// Whether the token is currently valid
    /// </summary>
    public bool IsValid { get; set; } = true;
}
