using Automaton.Studio.Pages.Install.Models;
using Automaton.Studio.Pages.Install.Services;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Pages.Install;

public class InstallViewModel
{
    private readonly InstallService installService;

    [Required]
    [StringLength(256)]
    public string Name { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; }

    public InstallViewModel(InstallService installService)
    {
        this.installService = installService;
    }

    public async Task Install()
    {
        var userDetails = new InstallCommand
        {
            Name = Name,
            Email = Email,
            Password = Password
        };

        await installService.Install(userDetails);
    }
}
