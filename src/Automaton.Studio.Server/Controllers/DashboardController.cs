using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly DashboardStatsService dashboardStatsService;

        public DashboardController(DashboardStatsService dashboardStatsService)
        {
            this.dashboardStatsService = dashboardStatsService;
        }

        [HttpGet("stats")]
        public async Task<ActionResult<Models.DashboardStats>> GetStats(CancellationToken cancellationToken = default)
        {
            var stats = await dashboardStatsService.GetDashboardStatsAsync(cancellationToken);
            return Ok(stats);
        }
    }
}