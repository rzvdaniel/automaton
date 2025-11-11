using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Pages.Authentication.Login;

public class LoginModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    public bool RememberMe { get; set; } = true;
}
