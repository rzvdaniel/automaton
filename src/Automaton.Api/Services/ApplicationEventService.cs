using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public class ApplicationEventService : IApplicationEventService
{
    private readonly ApplicationDbContext dbContext;
    private readonly Serilog.ILogger logger;

    public ApplicationEventService(ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
        logger = Serilog.Log.ForContext<ApplicationEventService>();
    }

    public async Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = new ApplicationEvent
            {
                Id = Guid.NewGuid(),
                AppId = request.AppId,
                Level = request.Level,
                Category = request.Category,
                EventType = request.EventType,
                Message = request.Message,
                MetadataJson = request.Metadata == null ? null : JsonSerializer.Serialize(request.Metadata),
                ActorUserId = request.ActorUserId,
                CreatedAtUtc = DateTime.UtcNow,
            };

            dbContext.ApplicationEvents.Add(entry);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to persist application event {EventType} for AppId={AppId}", request.EventType, request.AppId);
        }
    }

    public async Task<PagedResult<ApplicationEventDto>> GetEventsAsync(Guid appId, ApplicationEventQuery query, CancellationToken cancellationToken = default)
    {
        var q = dbContext.ApplicationEvents
            .AsNoTracking()
            .Where(x => x.AppId == appId);

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            q = q.Where(x => x.Category == query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.Level))
        {
            q = q.Where(x => x.Level == query.Level);
        }

        if (query.From.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc <= query.To.Value);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ApplicationEventDto
            {
                Id = x.Id,
                AppId = x.AppId,
                Level = x.Level,
                Category = x.Category,
                EventType = x.EventType,
                Message = x.Message,
                MetadataJson = x.MetadataJson,
                ActorUserId = x.ActorUserId,
                CreatedAtUtc = x.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ApplicationEventDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }
}
