using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Logging;
using Automaton.Studio.Server.Services.Interfaces;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Automaton.Studio.Server.Services;

public class AuditEventService : IAuditEventService
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SensitiveMetadataKeys =
    [
        "apikey",
        "rawkey",
        "key",
        "secret",
        "token",
        "password",
        "hash",
        "connectionstring",
        "authorization"
    ];
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
        AuditEventWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var actorUserId = request.ActorUserId ?? _userContextService.GetUserId();
            var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            var metadataJson = SanitizeMetadataJson(request.Metadata);
            var eventType = string.IsNullOrWhiteSpace(request.EventType)
                ? BuildEventType(request.Action, request.EntityType)
                : request.EventType;

            var auditEvent = new AuditEvent
            {
                Id = Guid.NewGuid(),
                Category = LogCategories.Audit,
                Action = request.Action,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                UserId = actorUserId == Guid.Empty ? null : actorUserId,
                TargetUserId = request.TargetUserId,
                UserName = _userContextService.GetUserName(),
                Outcome = request.Outcome,
                EventType = eventType,
                Message = request.Message,
                MetadataJson = metadataJson,
                IpAddress = ipAddress,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.AuditEvents.Add(auditEvent);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.Debug(
                "Audit event written: {EventType} {EntityType} {EntityId}",
                auditEvent.EventType,
                request.EntityType,
                request.EntityId);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to write audit event {EntityType} {Action} {EntityId}",
                request.EntityType,
                request.Action,
                request.EntityId);

            if (request.FailOnError)
            {
                throw;
            }
        }
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
        await WriteAsync(
            new AuditEventWriteRequest
            {
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Message = message,
                Metadata = metadata,
                Outcome = outcome,
                FailOnError = failOnError
            },
            cancellationToken);
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

    internal static string? SanitizeMetadataJson(object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var metadataNode = JsonSerializer.SerializeToNode(metadata, MetadataJsonOptions);
        SanitizeMetadataNode(metadataNode);
        return metadataNode?.ToJsonString(MetadataJsonOptions);
    }

    private static void SanitizeMetadataNode(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (property.Key is null)
                {
                    continue;
                }

                if (IsSensitiveMetadataKey(property.Key))
                {
                    jsonObject[property.Key] = "[REDACTED]";
                    continue;
                }

                SanitizeMetadataNode(property.Value);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                SanitizeMetadataNode(item);
            }
        }
    }

    private static bool IsSensitiveMetadataKey(string key)
    {
        var normalizedKey = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SensitiveMetadataKeys.Any(normalizedKey.Contains);
    }
}
