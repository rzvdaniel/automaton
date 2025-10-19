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

    public async Task<RunnerModel?> GetPythonConfig(Guid runnerId)
    {
        try
        {
            var url = $"{configService.RunnersUrl}/{runnerId}/python-config";
            var result = await httpClient.GetAsync(url);

            result.EnsureSuccessStatusCode();

            var config = await result.Content.ReadAsAsync<RunnerModel>();
            return config;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get Python configuration for runner {RunnerId}", runnerId);
            return null;
        }
    }

    public async Task<bool> UpdatePythonConfig(Guid runnerId, RunnerModel config)
    {
        try
        {
            var url = $"{configService.RunnersUrl}/{runnerId}/python-config";
            var result = await httpClient.PutAsJsonAsync(url, new
            {
                pythonDllPath = config.PythonDllPath,
                pythonPaths = config.PythonPaths,
                autoDetectPythonPaths = config.AutoDetectPythonPaths
            });

            result.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update Python configuration for runner {RunnerId}", runnerId);
            return false;
        }
    }
}
