using AutoMapper;
using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public class FlowSettingsService
{
    private HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly IMapper mapper;
    private readonly ILogger logger;

    public FlowSettingsService
    (
        ConfigurationService configService,
        IMapper mapper,
        HttpClient httpClient
    )
    {
        this.logger = Log.ForContext<FlowExecutionsService>(); ;
        this.configService = configService;
        this.httpClient = httpClient;
        this.mapper = mapper;
    }

    public async Task<FlowSettings> GetDashboardDetails(Guid flowId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.FlowsUrl}/settings/{flowId}");

            result.EnsureSuccessStatusCode();

            var dashboardResult = await result.Content.ReadAsAsync<FlowSettings>();

            return dashboardResult;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load flow execution list");

            throw;
        }
    }
}
