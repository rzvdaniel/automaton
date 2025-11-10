using Automaton.Client.Auth.Services;
using Automaton.Studio.Pages.Install.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Automaton.Studio.Pages.Install.Services;

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
                var response = JsonSerializer.Deserialize<InstallationStatusResponse>(content, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
