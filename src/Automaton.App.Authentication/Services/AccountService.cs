using Automaton.App.Authentication.Models;
using Automaton.Client.Auth.Services;
using System.Net.Http.Json;

namespace Automaton.App.Authentication.Services;

public class AccountService
{
    private readonly HttpClient httpClient;
    private readonly ClientAuthConfigurationService clientAuthConfigService;

    public AccountService(HttpClient httpClient, ClientAuthConfigurationService clientAuthConfigService)
    {
        this.httpClient = httpClient;
        this.clientAuthConfigService = clientAuthConfigService;
    }

    public async Task Register(UserRegister userRegistration)
    {
        var result = await httpClient.PostAsJsonAsync(clientAuthConfigService.RegisterUserUrl, userRegistration);

        result.EnsureSuccessStatusCode();
    }

    public async Task ForgotUserPassword(ForgotUserPasswordModel model)
    {
        var result = await httpClient.PostAsJsonAsync(clientAuthConfigService.ForgotUserPasswordUrl, model);

        result.EnsureSuccessStatusCode();
    }

    public async Task ResetUserPassword(ResetUserPasswordModel model)
    {
        var result = await httpClient.PostAsJsonAsync(clientAuthConfigService.ResetUserPasswordUrl, model);

        result.EnsureSuccessStatusCode();
    }
}
