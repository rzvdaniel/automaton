using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers
{
    public class RecentActivitiesController : BaseController
    {
        private readonly RecentActivityService recentActivityService;

        public RecentActivitiesController(RecentActivityService recentActivityService)
        {
            this.recentActivityService = recentActivityService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<RecentActivity>>> Get(int count = 20, CancellationToken cancellationToken = default)
        {
            var activities = await recentActivityService.GetRecentActivitiesAsync(count, cancellationToken);
            return Ok(activities);
        }

        [HttpGet("recent/{count}")]
        public async Task<ActionResult<IEnumerable<RecentActivity>>> GetRecent(int count, CancellationToken cancellationToken = default)
        {
            var activities = await recentActivityService.GetRecentActivitiesAsync(count, cancellationToken);
            return Ok(activities);
        }
    }
}