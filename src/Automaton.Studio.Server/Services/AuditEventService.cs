using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Logging;
using Automaton.Studio.Server.Services.Interfaces;
using Serilog;
using System.Text;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class AuditEventService : IAuditEventService
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);
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
        object? metadata = null,
        string outcome = "Success",
        bool failOnError = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _userContextService.GetUserId();
            var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, MetadataJsonOptions);
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
                Outcome = outcome,
                EventType = eventType,
                Message = message,
                MetadataJson = metadataJson,
                IpAddress = ipAddress,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.AuditEvents.Add(auditEvent);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.Debug(
                "Audit event written: {EventType} {EntityType} {EntityId}",
                auditEvent.EventType,
                entityType,
                entityId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to write audit event {EntityType} {Action} {EntityId}",
                entityType,
                action,
                entityId);

            if (failOnError)
            {
                throw;
            }
        }
    }

    private static string BuildEventType(string action, string entityType)
    {
        return $"{NormalizeEventPart(entityType)}{NormalizeEventPart(action)}";
    }

    private static string NormalizeEventPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var capitalizeNext = true;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        return builder.ToString();
    }
}
