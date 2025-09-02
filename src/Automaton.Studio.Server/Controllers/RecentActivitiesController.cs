using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers
{
    public class RecentActivitiesController : BaseController
    {
        private readonly RecentActivityService recentActivityService;

        public RecentActivitiesController(RecentActivityService recentActivityService)
        {
            this.recentActivityService = recentActivityService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Models.RecentActivity>>> Get(int count = 20, CancellationToken cancellationToken = default)
        {
            var activities = await recentActivityService.GetRecentActivitiesAsync(count, cancellationToken);
            return Ok(activities);
        }

        [HttpGet("recent/{count}")]
        public async Task<ActionResult<IEnumerable<Models.RecentActivity>>> GetRecent(int count, CancellationToken cancellationToken = default)
        {
            var activities = await recentActivityService.GetRecentActivitiesAsync(count, cancellationToken);
            return Ok(activities);
        }
    }
}