using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Logging;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class AuditEventService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserContextService _userContextService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Serilog.ILogger _logger;

    public AuditEventService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _userContextService = userContextService;
        _httpContextAccessor = httpContextAccessor;
        _logger = Serilog.Log.ForContext<AuditEventService>();
    }

    public async Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        string message,
        object? properties = null,
        LogEventLevel level = LogEventLevel.Information,
        CancellationToken cancellationToken = default)
    {
        var userId = _userContextService.GetUserId();
        var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var propertiesJson = properties is null ? null : JsonSerializer.Serialize(properties);
        var eventType = BuildEventType(action, entityType);

        var auditEvent = new AuditEvent
        {
            Id = Guid.NewGuid(),
            Category = LogCategories.Audit,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            UserId = userId == Guid.Empty ? null : userId,
            UserName = _userContextService.GetUserName(),
            Level = level.ToString(),
            EventType = eventType,
            Message = message,
            PropertiesJson = propertiesJson,
            IpAddress = ipAddress,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.AuditEvents.Add(auditEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        using (LogContext.PushProperty("LogCategory", LogCategories.Audit))
        using (LogContext.PushProperty("AuditAction", action))
        using (LogContext.PushProperty("AuditEntityType", entityType))
        using (LogContext.PushProperty("AuditEntityId", entityId))
        {
            _logger.Write(level, "{AuditMessage}", message);
        }
    }

    public async Task CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(retention);
        var expiredEvents = await _dbContext.AuditEvents
            .Where(a => a.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (expiredEvents.Count == 0)
        {
            return;
        }

        _dbContext.AuditEvents.RemoveRange(expiredEvents);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildEventType(string action, string entityType)
    {
        return $"{entityType}.{action}".ToLowerInvariant();
    }
}
