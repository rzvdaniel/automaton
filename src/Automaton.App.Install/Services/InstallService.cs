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

    public async Task<bool> IsInstallationComplete()
    {
        try
        {
            var result = await httpClient.GetAsync($"{clientAuthConfigService.BaseUrl}/api/install/status");
            
            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var response = System.Text.Json.JsonSerializer.Deserialize<InstallationStatusResponse>(content, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return response?.InstallationComplete ?? false;
            }
            
            return false;
        }
        catch
        {
            // If we can't check status, assume installation is not complete
            return false;
        }
    }
}
