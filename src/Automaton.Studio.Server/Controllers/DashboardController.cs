using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly DashboardStatsService dashboardStatsService;

        public DashboardController(DashboardStatsService dashboardStatsService)
        {
            this.dashboardStatsService = dashboardStatsService;
        }

        [HttpGet("stats")]
        public async Task<ActionResult<DashboardStats>> GetStats(CancellationToken cancellationToken = default)
        {
            var stats = await dashboardStatsService.GetDashboardStatsAsync(cancellationToken);
            return Ok(stats);
        }
    }
}