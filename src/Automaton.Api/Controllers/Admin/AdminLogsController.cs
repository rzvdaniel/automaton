using Automaton.Api.Data;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Controllers.Admin;

/// <summary>
/// Admin endpoint to query persisted audit events.
/// </summary>
[ApiController]
[Route("api/admin/logs")]
[Authorize(Policy = "Administrators")]
public class AdminLogsController : ControllerBase
{
    private static readonly string[] EntityDisplayMetadataKeys =
    [
        "name",
        "appName",
        "runnerName",
        "userName",
        "email",
        "keyPrefix"
    ];

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

        var dbQuery = ApplyFilters(dbContext.AuditEvents.AsNoTracking(), query);

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
                EntityDisplay = GetEntityDisplay(x.EntityType, x.EntityId, x.MetadataJson),
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

    private static IQueryable<Entities.AuditEvent> ApplyFilters(
        IQueryable<Entities.AuditEvent> dbQuery,
        AuditEventQuery query)
    {

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

        return ApplySearchFilter(dbQuery, query.Search);
    }

    private static IQueryable<Entities.AuditEvent> ApplySearchFilter(
        IQueryable<Entities.AuditEvent> dbQuery,
        string? searchText)
    {
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim();
            dbQuery = dbQuery.Where(x =>
                x.Message.Contains(search) ||
                x.EventType.Contains(search) ||
                x.EntityType.Contains(search) ||
                (x.UserName != null && x.UserName.Contains(search)) ||
                (x.MetadataJson != null && x.MetadataJson.Contains(search)));
        }

        return dbQuery;
    }

    [HttpGet("event-types")]
    public async Task<ActionResult<List<string>>> GetEventTypes(CancellationToken cancellationToken)
    {
        var eventTypes = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.EventType != null && x.EventType != string.Empty)
            .Select(x => x.EventType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return Ok(eventTypes);
    }

    [HttpGet("entity-types")]
    public async Task<ActionResult<List<string>>> GetEntityTypes(CancellationToken cancellationToken)
    {
        var entityTypes = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityType != null && x.EntityType != string.Empty)
            .Select(x => x.EntityType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return Ok(entityTypes);
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

    private static string? GetEntityDisplay(string entityType, Guid? entityId, string? metadataJson)
    {
        var metadataName = TryReadMetadataDisplayName(metadataJson);
        if (!string.IsNullOrWhiteSpace(metadataName))
        {
            return metadataName;
        }

        return entityId?.ToString();
    }

    private static string? TryReadMetadataDisplayName(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        var trimmed = metadataJson.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            foreach (var propertyName in EntityDisplayMetadataKeys)
            {
                if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    var value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // MetadataJson is user-facing display context here, not a strict contract.
            // If it is malformed, fall back to the entity id instead of failing the page.
            return null;
        }

        return null;
    }
}
