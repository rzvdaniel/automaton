using AntDesign;
using Automaton.App.Authentication.Models;
using Automaton.App.Authentication.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Automaton.App.Authentication.Pages.ResetPassword;

partial class ResetPassword : ComponentBase
{
    private bool loading = false;

    [Inject] NavigationManager NavigationManager { get; set; }
    [Inject] AccountService AccountService { get; set; }
    [Inject] private MessageService MessageService { get; set; }

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = new();

    [SupplyParameterFromQuery]
    private string? Email { get; set; }

    [SupplyParameterFromQuery]
    private string? Code { get; set; }

    protected override void OnInitialized()
    {
        if (Code is null)
        {
            NavigationManager.NavigateTo("Account/InvalidPasswordReset");
        }

        Input.Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));

        if (Email is null)
        {
            NavigationManager.NavigateTo("Account/InvalidPasswordReset");
        }

        Input.Email = Email;
    }

    private async Task OnFinish(EditContext editContext)
    {
        try
        {
            loading = true;

            var model = new ResetUserPasswordModel
            {
                Email = Input.Email,
                Password = Input.Password,
                Code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(Input.Code))
            };

            await AccountService.ResetUserPassword(model);

            NavigationManager.NavigateTo("Account/ResetPasswordConfirmation");
        }
        catch (Exception ex)
        {
            await MessageService.Error(Resources.Errors.ResetPasswordFailed);
        }
        finally
        {
            loading = false;
        }
    }

    private void OnFinishFailed(EditContext editContext)
    {
        Task.Run(() => MessageService.Error(Resources.Errors.ResetPasswordFailed));
    }

    public class InputModel
    {
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

        [Required]
        public string Code { get; set; }
    }
}
