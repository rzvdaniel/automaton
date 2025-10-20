using AutoMapper;
using Automaton.Studio.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading.Tasks;

namespace Automaton.Studio.Services;

public class RunnerService
{
    private HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly IMapper mapper;
    private readonly ILogger<RunnerService> logger;

    public RunnerService
    (
        ConfigurationService configService,
        IMapper mapper,
        HttpClient httpClient,
        ILogger<RunnerService> logger
    )
    {
        this.logger = logger;
        this.configService = configService;
        this.httpClient = httpClient;
        this.mapper = mapper;
    }

    public async Task<ICollection<RunnerModel>> List()
    {
        ICollection<RunnerModel> runners = new List<RunnerModel>();

        try
        {
            var result = await httpClient.GetAsync(configService.RunnersUrl);

            result.EnsureSuccessStatusCode();

            runners = await result.Content.ReadAsAsync<ICollection<RunnerModel>>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load runners list");
        }

        return runners;
    }

    public async Task Delete(Guid id)
    {
        try
        {
            var result = await httpClient.DeleteAsync($"{configService.RunnersUrl}/{id}");
            result.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete runner {RunnerId}", id);
            throw;
        }
    }

    /// <summary>
    /// Update runner name
    /// </summary>
    public async Task Update(Guid id, string name)
    {
        try
        {
            var runnerData = new { Id = id, Name = name };
            var result = await httpClient.PutAsJsonAsync($"{configService.RunnersUrl}/{id}", runnerData);
            result.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update runner {RunnerId}", id);
            throw;
        }
    }

    /// <summary>
    /// Get script engine settings for a specific runner
    /// </summary>
    public async Task<string?> GetScriptEngineSettings(Guid id)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.RunnersUrl}/{id}/script-settings");
            result.EnsureSuccessStatusCode();

            // Read JSON string directly to avoid serialization issues
            var jsonString = await result.Content.ReadAsStringAsync();
            
            if (!string.IsNullOrWhiteSpace(jsonString))
            {
                return jsonString;
            }

            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogInformation("No script engine settings found for runner {RunnerId}", id);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get script engine settings for runner {RunnerId}", id);
            throw;
        }
    }

    /// <summary>
    /// Update script engine settings for a specific runner
    /// </summary>
    public async Task UpdateScriptEngineSettings(Guid id, string settingsJson)
    {
        try
        {
            var content = new StringContent(settingsJson, System.Text.Encoding.UTF8, "application/json");
            var result = await httpClient.PutAsync($"{configService.RunnersUrl}/{id}/script-settings", content);
            result.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update script engine settings for runner {RunnerId}", id);
            throw;
        }
    }
}
