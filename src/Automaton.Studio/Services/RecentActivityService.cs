using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Serilog;

namespace Automaton.Studio.Services
{
    public class RecentActivityService
    {
        private readonly HttpClient httpClient;
        private readonly ConfigurationService configService;
        private readonly ILogger logger;

        public RecentActivityService(HttpClient httpClient, ConfigurationService configService)
        {
            this.httpClient = httpClient;
            this.configService = configService;
            this.logger = Log.ForContext<RecentActivityService>();
        }

        public async Task<IEnumerable<RecentActivity>> GetRecentActivitiesAsync(int count = 20)
        {
            try
            {
                var response = await httpClient.GetAsync($"{configService.BaseUrl}/api/recentactivities/recent/{count}");
                
                if (response.IsSuccessStatusCode)
                {
                    var activities = await response.Content.ReadAsAsync<IEnumerable<RecentActivity>>();
                    return activities ?? new List<RecentActivity>();
                }
                
                logger.Warning("Failed to fetch recent activities. Status: {StatusCode}", response.StatusCode);
                return new List<RecentActivity>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error fetching recent activities");
                return new List<RecentActivity>();
            }
        }
    }
}