using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public class FlowsService
{
    private HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly Serilog.ILogger logger;

    public FlowsService
    (
        ConfigurationService configService,
        HttpClient httpClient
    )
    {
        logger = Log.ForContext<FlowsService>();
        this.configService = configService;
        this.httpClient = httpClient;
    }

    public async Task<ICollection<FlowInfo>> List(Guid? projectId = null)
    {
        try
        {
            var url = configService.FlowsUrl;
            if (projectId.HasValue)
            {
                url += $"?projectId={projectId.Value}";
            }

            var result = await httpClient.GetAsync(url);

            result.EnsureSuccessStatusCode();

            var flows = await result.Content.ReadAsAsync<ICollection<FlowInfo>>();

            return flows;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading flows list.");
            throw;
        }   
    }

    public async Task<bool> Exists(string name)
    {
        var result = await httpClient.GetAsync($"{configService.FlowsUrl}/exists/{name}");

        var exists = result.StatusCode != System.Net.HttpStatusCode.NotFound;

        return exists;
    }
}
