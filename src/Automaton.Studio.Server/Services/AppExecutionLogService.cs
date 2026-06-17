using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;

using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public class AppExecutionLogService : IAppExecutionLogService
{
    private readonly ApplicationDbContext dbContext;
    private readonly Serilog.ILogger logger;

    public AppExecutionLogService(ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
        this.logger = Serilog.Log.ForContext<AppExecutionLogService>();
    }

    public async Task LogExecutionAsync(AppExecutionLogRequest request)
    {
        try
        {
            var entry = new AppExecutionLog
            {
                Id = Guid.NewGuid(),
                AppId = request.AppId,
                ControllerName = request.ControllerName,
                ActionName = request.ActionName,
                RequestPayload = request.RequestPayload,
                ResponsePayload = request.ResponsePayload,
                Success = request.Success,
                ErrorMessage = request.ErrorMessage,
                DurationMs = request.DurationMs,
                UserId = request.UserId,
                IpAddress = request.IpAddress,
                CreatedAt = DateTime.UtcNow,
            };

            dbContext.AppExecutionLogs.Add(entry);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to persist execution log for AppId={AppId}", request.AppId);
        }
    }

    public async Task<PagedResult<AppExecutionLogDto>> GetLogsAsync(Guid appId, AppExecutionLogQuery query)
    {
        var q = dbContext.AppExecutionLogs
            .Where(l => l.AppId == appId);

        if (query.Success.HasValue)
            q = q.Where(l => l.Success == query.Success.Value);

        if (!string.IsNullOrWhiteSpace(query.ControllerName))
            q = q.Where(l => l.ControllerName == query.ControllerName);

        if (query.From.HasValue)
            q = q.Where(l => l.CreatedAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(l => l.CreatedAt <= query.To.Value);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(l => l.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(l => new AppExecutionLogDto
            {
                Id = l.Id,
                AppId = l.AppId,
                ControllerName = l.ControllerName,
                ActionName = l.ActionName,
                RequestPayload = l.RequestPayload,
                ResponsePayload = l.ResponsePayload,
                Success = l.Success,
                ErrorMessage = l.ErrorMessage,
                DurationMs = l.DurationMs,
                UserId = l.UserId,
                IpAddress = l.IpAddress,
                CreatedAt = l.CreatedAt,
            })
            .ToListAsync();

        return new PagedResult<AppExecutionLogDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }
}
