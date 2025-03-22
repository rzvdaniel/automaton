using Automaton.App.Install.Models;
using Automaton.Client.Auth.Services;
using System.Net.Http.Json;

namespace Automaton.App.Install.Services;

public class InstallService
{
    private readonly HttpClient httpClient;
    private readonly ClientAuthConfigurationService clientAuthConfigService;

    public InstallService(HttpClient httpClient, ClientAuthConfigurationService clientAuthConfigService)
    {
        this.httpClient = httpClient;
        this.clientAuthConfigService = clientAuthConfigService;
    }

    public async Task Install(InstallCommand userRegistration)
    {
        var result = await httpClient.PostAsJsonAsync($"{clientAuthConfigService.BaseUrl}/api/install", userRegistration);

        result.EnsureSuccessStatusCode();
    }
}
