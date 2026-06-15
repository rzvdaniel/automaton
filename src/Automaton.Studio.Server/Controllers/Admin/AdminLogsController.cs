using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers.Admin;

/// <summary>
/// Admin endpoint to query persisted audit events.
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

    [HttpGet("audit-events")]
    public async Task<ActionResult<PagedResult<AuditEventDto>>> GetAuditEvents(
        [FromQuery] AuditEventQuery query,
        CancellationToken cancellationToken)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 1, 200);

        var dbQuery = dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            dbQuery = dbQuery.Where(x => x.EventType == query.EventType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            dbQuery = dbQuery.Where(x => x.EntityType == query.EntityType);
        }

        if (query.EntityId.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.EntityId == query.EntityId);
        }

        if (query.UserId.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.UserId == query.UserId);
        }

        if (query.TargetUserId.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.TargetUserId == query.TargetUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Outcome))
        {
            dbQuery = dbQuery.Where(x => x.Outcome == query.Outcome);
        }

        if (query.FromUtc.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.CreatedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.CreatedAtUtc <= query.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            dbQuery = dbQuery.Where(x =>
                x.Message.Contains(search) ||
                x.EventType.Contains(search) ||
                x.EntityType.Contains(search) ||
                (x.UserName != null && x.UserName.Contains(search)) ||
                (x.MetadataJson != null && x.MetadataJson.Contains(search)));
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new AuditEventDto
            {
                Id = x.Id,
                Category = x.Category,
                Action = x.Action,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                UserId = x.UserId,
                TargetUserId = x.TargetUserId,
                UserName = x.UserName,
                Outcome = x.Outcome,
                EventType = x.EventType,
                Message = x.Message,
                MetadataJson = x.MetadataJson,
                IpAddress = x.IpAddress,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<AuditEventDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    /// <summary>
    /// Get distinct log levels present in the database (for filter dropdown).
    /// </summary>
    [HttpGet("levels")]
    public async Task<ActionResult<List<string>>> GetLogLevels()
    {
        var levels = await dbContext.AuditEvents
            .Select(l => l.Outcome)
            .Distinct()
            .OrderBy(l => l)
            .ToListAsync();

        return Ok(levels);
    }
}
