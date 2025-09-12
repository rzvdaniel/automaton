using System.Net.Http;
using System.Text.Json;
using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

/// <summary>
/// Simplified onboarding service - no localStorage, no polling, just API-based checks
/// </summary>
public class OnboardingService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _configurationService;

    public OnboardingService(HttpClient httpClient, ConfigurationService configurationService)
    {
        _httpClient = httpClient;
        _configurationService = configurationService;
    }

    /// <summary>
    /// Check if user has any registered runners (regardless of online/offline status)
    /// This determines if onboarding should be shown
    /// </summary>
    /// <returns>True if user has completed setup (has registered runners)</returns>
    public async Task<bool> HasRegisteredRunnersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_configurationService.BaseUrl}/api/runners/status");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var statusData = JsonSerializer.Deserialize<RunnerStatusResponse>(jsonResponse, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                return statusData?.TotalRunners > 0;
            }
            
            return false;
        }
        catch
        {
            // On error, assume setup is complete to avoid showing onboarding unnecessarily
            return true;
        }
    }

    /// <summary>
    /// Determine if onboarding should be shown based on registered runners
    /// </summary>
    public async Task<bool> ShouldShowOnboardingAsync()
    {
        var hasRunners = await HasRegisteredRunnersAsync();
        return !hasRunners; // Show onboarding only if no runners registered
    }

    /// <summary>
    /// Safe version that can be called during prerendering
    /// </summary>
    public async Task<bool> TryShouldShowOnboardingAsync()
    {
        try
        {
            return await ShouldShowOnboardingAsync();
        }
        catch
        {
            return false; // Don't show onboarding on error
        }
    }
}