using AntDesign;
using Automaton.App.Authentication.Models;
using Automaton.App.Authentication.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.ComponentModel.DataAnnotations;

namespace Automaton.App.Authentication.Pages.ForgotPassword;

partial class ForgotPassword : ComponentBase
{
    private bool loading = false;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = new();

    [Inject] NavigationManager NavigationManager { get; set; }
    [Inject] AccountService AccountService { get; set; }
    [Inject] private MessageService MessageService { get; set; }

    private async Task OnFinish(EditContext editContext)
    {
        try
        {
            var model = new ForgotUserPasswordModel
            {
                Email = Input.Email
            };

            await AccountService.ForgotUserPassword(model);

            NavigationManager.NavigateTo("Account/ForgotPasswordConfirmation");
        }
        catch (Exception ex)
        {
            await MessageService.Error(Resources.Errors.ForgotPasswordFailed);
        }
        finally
        {
            loading = false;
        }
    }

    private void OnFinishFailed(EditContext editContext)
    {
        Task.Run(() => MessageService.Error(Resources.Errors.ForgotPasswordFailed));
    }

    private sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
