using Automaton.App.Authentication.Models;
using Automaton.App.Authentication.Services;
using System.ComponentModel.DataAnnotations;

namespace Automaton.App.Authentication.Pages.Register;

public class UserRegisterViewModel
{
    private readonly AccountService registrationService;

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

    [MustBeTrue(ErrorMessage = "You must agree with the terms and privacy policy.")]
    public bool AgreeWithTermsPrivacyPolicy { get; set; }

    public UserRegisterViewModel(AccountService registrationService)
    {
        this.registrationService = registrationService;
    }

    public async Task Register()
    {
        var userDetails = new UserRegister
        {
            Name = Name,
            Email = Email,
            Password = Password
        };

        await registrationService.Register(userDetails);
    }
}
