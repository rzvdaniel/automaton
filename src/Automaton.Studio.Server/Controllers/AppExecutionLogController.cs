using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Provides access to app controller execution logs (runtime diagnostics).
/// </summary>
[ApiController]
[Route("api/apps/{appId:guid}/execution-logs")]
[Authorize]
public class AppExecutionLogController : ControllerBase
{
    private readonly IAppExecutionLogService logService;

    public AppExecutionLogController(IAppExecutionLogService logService)
    {
        this.logService = logService;
    }

    /// <summary>
    /// Get execution logs for a specific app, with optional filters.
    /// </summary>
    /// <param name="appId">App ID</param>
    /// <param name="success">Filter by success/failure (null = all)</param>
    /// <param name="controllerName">Filter by controller name</param>
    /// <param name="from">Start of date range (UTC)</param>
    /// <param name="to">End of date range (UTC)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Results per page (max 200)</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AppExecutionLogDto>>> GetLogs(
        Guid appId,
        [FromQuery] bool? success = null,
        [FromQuery] string? controllerName = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = new AppExecutionLogQuery
        {
            Success = success,
            ControllerName = controllerName,
            From = from,
            To = to,
            Page = Math.Max(1, page),
            PageSize = Math.Min(200, Math.Max(1, pageSize)),
        };

        var result = await logService.GetLogsAsync(appId, query);
        return Ok(result);
    }
}
