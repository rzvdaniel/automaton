using AutoMapper;
using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public class FlowDashboardService
{
    private HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly IMapper mapper;
    private readonly ILogger logger;

    public FlowDashboardService
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

    public async Task<FlowDashboard> GetDashboardDetails(Guid flowId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.FlowsUrl}/dashboard/{flowId}");

            result.EnsureSuccessStatusCode();

            var dashboardResult = await result.Content.ReadAsAsync<FlowDashboard>();

            return dashboardResult;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load flow execution list");

            throw;
        }
    }
}
