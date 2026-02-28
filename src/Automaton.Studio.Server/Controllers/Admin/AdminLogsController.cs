using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers.Admin;

/// <summary>
/// Admin endpoint to query the Serilog structured log table.
/// </summary>
[ApiController]
[Route("api/admin/logs")]
[Authorize(Policy = "Administrators")]
public class AdminLogsController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;

    public AdminLogsController(ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    /// <summary>
    /// Query structured logs with optional filters.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminLogEntryDto>>> GetLogs(
        [FromQuery] string? level = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var q = dbContext.Logs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(level))
            q = q.Where(l => l.Level == level);

        if (from.HasValue)
            q = q.Where(l => l.Timestamp >= from.Value);

        if (to.HasValue)
            q = q.Where(l => l.Timestamp <= to.Value);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(l => (l.Message != null && l.Message.Contains(search)) ||
                             (l.Exception != null && l.Exception.Contains(search)));

        var total = await q.CountAsync();

        var safePageSize = Math.Min(500, Math.Max(1, pageSize));
        var safePage = Math.Max(1, page);

        var items = await q
            .OrderByDescending(l => l.Timestamp)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(l => new AdminLogEntryDto
            {
                Id = l.Id,
                Message = l.Message,
                Level = l.Level,
                Timestamp = l.Timestamp,
                Exception = l.Exception,
                Properties = l.Properties,
                UserName = l.UserName,
            })
            .ToListAsync();

        return Ok(new PagedResult<AdminLogEntryDto>
        {
            Items = items,
            TotalCount = total,
            Page = safePage,
            PageSize = safePageSize,
        });
    }

    /// <summary>
    /// Get distinct log levels present in the database (for filter dropdown).
    /// </summary>
    [HttpGet("levels")]
    public async Task<ActionResult<List<string>>> GetLogLevels()
    {
        var levels = await dbContext.Logs
            .Where(l => l.Level != null)
            .Select(l => l.Level)
            .Distinct()
            .OrderBy(l => l)
            .ToListAsync();

        return Ok(levels);
    }
}
