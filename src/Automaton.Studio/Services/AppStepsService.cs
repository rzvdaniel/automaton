using Automaton.Studio.Models;
using System.Net.Http.Json;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for managing App-Step relationships in the frontend
/// </summary>
public class AppStepsService
{
    private readonly HttpClient httpClient;

    public AppStepsService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    /// <summary>
    /// Get steps linked to an app
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetLinkedStepsAsync(Guid appId)
    {
        var response = await httpClient.GetFromJsonAsync<List<LinkedStepInfo>>(
            $"api/apps/{appId}/steps");
        return response ?? new List<LinkedStepInfo>();
    }

    /// <summary>
    /// Get available steps that can be linked
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetAvailableStepsAsync(Guid appId)
    {
        var response = await httpClient.GetFromJsonAsync<List<LinkedStepInfo>>(
            $"api/apps/{appId}/steps/available");
        return response ?? new List<LinkedStepInfo>();
    }

    /// <summary>
    /// Link a step to an app
    /// </summary>
    public async Task LinkStepAsync(Guid appId, Guid stepId)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"api/apps/{appId}/steps/{stepId}/link", 
            new { });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Link multiple steps to an app
    /// </summary>
    public async Task LinkStepsAsync(Guid appId, IEnumerable<Guid> stepIds)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"api/apps/{appId}/steps/link-multiple",
            new { StepIds = stepIds });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Unlink a step from an app
    /// </summary>
    public async Task UnlinkStepAsync(Guid appId, Guid stepId)
    {
        var response = await httpClient.DeleteAsync(
            $"api/apps/{appId}/steps/{stepId}");
        response.EnsureSuccessStatusCode();
    }
}
