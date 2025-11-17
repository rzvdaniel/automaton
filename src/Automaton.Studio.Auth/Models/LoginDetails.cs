using System.ComponentModel.DataAnnotations;

namespace Automaton.Client.Auth.Models;

public class LoginDetails
{
    [Required]
    public string Email { get; set; }

    [Required]
    public string Password { get; set; }

    public bool RememberMe { get; set; } = true; 
}
