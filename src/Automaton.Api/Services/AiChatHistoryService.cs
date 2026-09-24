using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public class AiChatHistoryService : IAiChatHistoryService
{
    private const string VerificationStrategyPrefix = "Verification strategy: ";
    private const string ExecutionPolicyPrefix = "Execution policy: ";

    private readonly ApplicationDbContext dataContext;
    private readonly ITokenBudgetService tokenBudgetService;
    private readonly Serilog.ILogger logger;

    public AiChatHistoryService(
        ApplicationDbContext dataContext, 
        ITokenBudgetService tokenBudgetService)
    {
        this.dataContext = dataContext;
        this.tokenBudgetService = tokenBudgetService;
        this.logger = Serilog.Log.ForContext<AiChatHistoryService>();
    }

    public async Task<PagedResult<ChatSessionDto>> GetSessionsAsync(
        ChatResourceType resourceType, Guid resourceId, Guid userId,
        int page = 1, int pageSize = 20, string? search = null)
    {
        try
        {
            var query = dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .Include(s => s.Messages)
                .Where(s => s.ResourceType == resourceType &&
                           s.ResourceId == resourceId &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => s.Title.Contains(search));

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(s => s.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    ResourceType = s.ResourceType,
                    ResourceId = s.ResourceId,
                    Title = s.Title,
                    Provider = s.Provider,
                    Model = s.Model,
                    IsActive = s.IsActive,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt,
                    MessageCount = s.Messages.Count,
                    TotalPromptsSubmitted = s.TotalPromptsSubmitted,
                    TotalApiCalls = s.TotalApiCalls,
                    TotalInputTokens = s.TotalInputTokens,
                    TotalOutputTokens = s.TotalOutputTokens,
                    TotalTokens = s.TotalTokens,
                    TotalCacheReadTokens = s.TotalCacheReadTokens,
                    TotalCacheCreationTokens = s.TotalCacheCreationTokens,
                    EffectiveInputTokens = Math.Max(0, s.TotalInputTokens - s.TotalCacheReadTokens),
                    CacheHitRate = s.TotalInputTokens > 0 ? Math.Min(1.0, (double)s.TotalCacheReadTokens / s.TotalInputTokens) : 0,
                    CacheHitPercent = s.TotalInputTokens > 0 ? Math.Min(100.0, ((double)s.TotalCacheReadTokens / s.TotalInputTokens) * 100.0) : 0,
                    ComplexityTier = s.ComplexityTier,
                    EffectiveVerificationStrategy = s.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(VerificationStrategyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(VerificationStrategyPrefix.Length))
                        .FirstOrDefault(),
                    EffectiveExecutionPolicy = s.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(ExecutionPolicyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(ExecutionPolicyPrefix.Length))
                        .FirstOrDefault()
                })
                .ToListAsync();

            return new PagedResult<ChatSessionDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting chat sessions for resource {ResourceType} {ResourceId}", resourceType, resourceId);
            throw;
        }
    }

    public async Task<ChatSessionDto?> GetSessionAsync(Guid sessionId, Guid userId)
    {
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .Include(s => s.Messages)
                .Where(s => s.Id == sessionId &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId))
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    ResourceType = s.ResourceType,
                    ResourceId = s.ResourceId,
                    Title = s.Title,
                    Provider = s.Provider,
                    Model = s.Model,
                    IsActive = s.IsActive,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt,
                    MessageCount = s.Messages.Count,
                    TotalPromptsSubmitted = s.TotalPromptsSubmitted,
                    TotalApiCalls = s.TotalApiCalls,
                    TotalInputTokens = s.TotalInputTokens,
                    TotalOutputTokens = s.TotalOutputTokens,
                    TotalTokens = s.TotalTokens,
                    TotalCacheReadTokens = s.TotalCacheReadTokens,
                    TotalCacheCreationTokens = s.TotalCacheCreationTokens,
                    EffectiveInputTokens = Math.Max(0, s.TotalInputTokens - s.TotalCacheReadTokens),
                    CacheHitRate = s.TotalInputTokens > 0 ? Math.Min(1.0, (double)s.TotalCacheReadTokens / s.TotalInputTokens) : 0,
                    CacheHitPercent = s.TotalInputTokens > 0 ? Math.Min(100.0, ((double)s.TotalCacheReadTokens / s.TotalInputTokens) * 100.0) : 0,
                    ComplexityTier = s.ComplexityTier,
                    EffectiveVerificationStrategy = s.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(VerificationStrategyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(VerificationStrategyPrefix.Length))
                        .FirstOrDefault(),
                    EffectiveExecutionPolicy = s.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(ExecutionPolicyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(ExecutionPolicyPrefix.Length))
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            return session;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting chat session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync(Guid sessionId, Guid userId)
    {
        try
        {
            // Verify user has access to the session
            var hasAccess = await dataContext.AiChatSessions
                .AnyAsync(s => s.Id == sessionId &&
                              s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (!hasAccess)
            {
                logger.Warning("User {UserId} does not have access to session {SessionId}", userId, sessionId);
                return new List<ChatMessageDto>();
            }

            var messages = await dataContext.AiChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    SessionId = m.SessionId,
                    Role = m.Role,
                    Content = m.Content,
                    MessageType = m.MessageType,
                    Metadata = m.Metadata,
                    InputTokens = m.InputTokens,
                    OutputTokens = m.OutputTokens,
                    TotalTokens = m.TotalTokens,
                    Model = m.Model,
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();

            return messages;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting messages for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<ChatSessionWithMessagesDto?> GetSessionWithMessagesAsync(Guid sessionId, Guid userId)
    {
        try
        {
            var session = await GetSessionAsync(sessionId, userId);
            if (session == null)
            {
                return null;
            }

            var messages = await GetMessagesAsync(sessionId, userId);

            return new ChatSessionWithMessagesDto
            {
                Session = session,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting session with messages {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<ChatSessionDto> GetOrCreateActiveSessionAsync(
        ChatResourceType resourceType,
        Guid resourceId,
        Guid userId,
        string? provider = null,
        string? model = null)
    {
        try
        {
            // Try to get the active session
            var activeSession = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .Include(s => s.Messages)
                .Where(s => s.ResourceType == resourceType &&
                           s.ResourceId == resourceId &&
                           s.IsActive &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId))
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    ResourceType = s.ResourceType,
                    ResourceId = s.ResourceId,
                    Title = s.Title,
                    Provider = s.Provider,
                    Model = s.Model,
                    IsActive = s.IsActive,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt,
                    MessageCount = s.Messages.Count,
                    TotalPromptsSubmitted = s.TotalPromptsSubmitted,
                    TotalApiCalls = s.TotalApiCalls,
                    TotalInputTokens = s.TotalInputTokens,
                    TotalOutputTokens = s.TotalOutputTokens,
                    TotalTokens = s.TotalTokens,
                    TotalCacheReadTokens = s.TotalCacheReadTokens,
                    TotalCacheCreationTokens = s.TotalCacheCreationTokens,
                    EffectiveInputTokens = Math.Max(0, s.TotalInputTokens - s.TotalCacheReadTokens),
                    CacheHitRate = s.TotalInputTokens > 0 ? Math.Min(1.0, (double)s.TotalCacheReadTokens / s.TotalInputTokens) : 0,
                    CacheHitPercent = s.TotalInputTokens > 0 ? Math.Min(100.0, ((double)s.TotalCacheReadTokens / s.TotalInputTokens) * 100.0) : 0,
                    EffectiveVerificationStrategy = s.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(VerificationStrategyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(VerificationStrategyPrefix.Length))
                        .FirstOrDefault(),
                    EffectiveExecutionPolicy = s.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(ExecutionPolicyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(ExecutionPolicyPrefix.Length))
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (activeSession != null)
            {
                return activeSession;
            }

            // No active session, create a new one
            var request = new CreateChatSessionRequest
            {
                ResourceType = resourceType,
                ResourceId = resourceId,
                Title = "New Chat",
                Provider = provider,
                Model = model
            };

            return await CreateSessionAsync(request, userId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting or creating active session for {ResourceType} {ResourceId}", resourceType, resourceId);
            throw;
        }
    }

    public async Task<ChatSessionDto> CreateSessionAsync(CreateChatSessionRequest request, Guid userId)
    {
        try
        {
            var now = DateTime.UtcNow;
            var sessionEntity = new AiChatSession
            {
                Id = Guid.NewGuid(),
                ResourceType = request.ResourceType,
                ResourceId = request.ResourceId,
                Title = request.Title ?? "New Chat",
                Provider = request.Provider,
                Model = request.Model,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            dataContext.AiChatSessions.Add(sessionEntity);

            // Link session to user
            var sessionUser = new AiChatSessionUser
            {
                SessionId = sessionEntity.Id,
                UserId = userId
            };

            dataContext.AiChatSessionUsers.Add(sessionUser);

            // Deactivate other sessions for this resource
            var otherSessions = await dataContext.AiChatSessions
                .Where(s => s.ResourceType == request.ResourceType &&
                           s.ResourceId == request.ResourceId &&
                           s.Id != sessionEntity.Id &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId))
                .ToListAsync();

            foreach (var session in otherSessions)
            {
                session.IsActive = false;
            }

            await dataContext.SaveChangesAsync();

            logger.Debug("Created new chat session {SessionId} for {ResourceType} {ResourceId}",
                sessionEntity.Id, request.ResourceType, request.ResourceId);


            return new ChatSessionDto
            {
                Id = sessionEntity.Id,
                ResourceType = sessionEntity.ResourceType,
                ResourceId = sessionEntity.ResourceId,
                Title = sessionEntity.Title,
                Provider = sessionEntity.Provider,
                Model = sessionEntity.Model,
                IsActive = sessionEntity.IsActive,
                CreatedAt = sessionEntity.CreatedAt,
                UpdatedAt = sessionEntity.UpdatedAt,
                MessageCount = 0,
                TotalPromptsSubmitted = 0,
                TotalInputTokens = 0,
                TotalOutputTokens = 0,
                TotalTokens = 0,
                TotalCacheReadTokens = 0,
                TotalCacheCreationTokens = 0,
                EffectiveInputTokens = 0,
                CacheHitRate = 0,
                CacheHitPercent = 0,
                EffectiveVerificationStrategy = null,
                EffectiveExecutionPolicy = null
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error creating chat session");
            throw;
        }
    }

    public async Task<bool> SetActiveSessionAsync(Guid sessionId, Guid userId)
    {
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId &&
                                         s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (session == null)
            {
                logger.Warning("Session {SessionId} not found or user {UserId} does not have access", sessionId, userId);
                return false;
            }

            // Deactivate other sessions for this resource
            var otherSessions = await dataContext.AiChatSessions
                .Where(s => s.ResourceType == session.ResourceType &&
                           s.ResourceId == session.ResourceId &&
                           s.Id != sessionId &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId))
                .ToListAsync();

            foreach (var otherSession in otherSessions)
            {
                otherSession.IsActive = false;
            }

            session.IsActive = true;
            session.UpdatedAt = DateTime.UtcNow;

            await dataContext.SaveChangesAsync();

            logger.Debug("Set session {SessionId} as active", sessionId);

            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error setting session {SessionId} as active", sessionId);
            throw;
        }
    }

    public async Task<bool> UpdateSessionTitleAsync(Guid sessionId, string title, Guid userId)
    {
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId &&
                                         s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (session == null)
            {
                logger.Warning("Session {SessionId} not found or user {UserId} does not have access", sessionId, userId);
                return false;
            }

            session.Title = title;
            session.UpdatedAt = DateTime.UtcNow;

            await dataContext.SaveChangesAsync();

            logger.Debug("Updated session {SessionId} title to {Title}", sessionId, title);

            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error updating session {SessionId} title", sessionId);
            throw;
        }
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId)
    {
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId &&
                                         s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (session == null)
            {
                logger.Warning("Session {SessionId} not found or user {UserId} does not have access", sessionId, userId);
                return false;
            }

            dataContext.AiChatSessions.Remove(session);
            await dataContext.SaveChangesAsync();

            logger.Debug("Deleted session {SessionId}", sessionId);

            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error deleting session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<ChatMessageDto> SaveMessageAsync(SaveChatMessageRequest request, Guid userId)
    {
        try
        {
            // Verify user has access to the session
            var hasAccess = await dataContext.AiChatSessions
                .AnyAsync(s => s.Id == request.SessionId &&
                              s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (!hasAccess)
            {
                logger.Warning("User {UserId} does not have access to session {SessionId}", userId, request.SessionId);
                throw new UnauthorizedAccessException("Access denied to this session");
            }

            // Calculate total tokens
            int? totalTokens = null;
            if (request.InputTokens.HasValue && request.OutputTokens.HasValue)
            {
                totalTokens = request.InputTokens.Value + request.OutputTokens.Value;
            }

            var messageEntity = new AiChatMessage
            {
                Id = Guid.NewGuid(),
                SessionId = request.SessionId,
                Role = request.Role,
                Content = request.Content,
                MessageType = request.MessageType,
                Metadata = request.Metadata,
                InputTokens = request.InputTokens,
                OutputTokens = request.OutputTokens,
                TotalTokens = totalTokens,
                Model = request.Model,
                CreatedAt = DateTime.UtcNow
            };

            dataContext.AiChatMessages.Add(messageEntity);

            // Update session metrics and get app resource for cascade update
            var session = await dataContext.AiChatSessions
                .FirstOrDefaultAsync(s => s.Id == request.SessionId);
            
            if (session != null)
            {
                // Update session activity timestamp
                session.LastActivityAt = DateTime.UtcNow;
                session.UpdatedAt = DateTime.UtcNow;

                // Count prompts (user messages only)
                if (request.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    session.TotalPromptsSubmitted++;
                }

                // Add token counts (only for AI-generated messages)
                if (request.InputTokens.HasValue)
                {
                    session.TotalInputTokens += request.InputTokens.Value;
                }
                if (request.OutputTokens.HasValue)
                {
                    session.TotalOutputTokens += request.OutputTokens.Value;
                }
                if (totalTokens.HasValue)
                {
                    session.TotalTokens += totalTokens.Value;
                }

                if (request.CacheReadTokens.HasValue) session.TotalCacheReadTokens += request.CacheReadTokens.Value;
                if (request.CacheCreationTokens.HasValue) session.TotalCacheCreationTokens += request.CacheCreationTokens.Value;

                // If this is a WebApp session, cascade updates to the app entity
                if (session.ResourceType == ChatResourceType.WebApp)
                {
                    var app = await dataContext.WebApps
                        .FirstOrDefaultAsync(w => w.Id == session.ResourceId);
                    
                    if (app != null)
                    {
                        app.LastChatActivityAt = DateTime.UtcNow;
                        
                        if (request.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                        {
                            app.TotalPromptsSubmittedInChat++;
                        }
                        
                        if (totalTokens.HasValue)
                        {
                            app.TotalChatTokensUsed += totalTokens.Value;
                        }
                    }
                }
            }

            await dataContext.SaveChangesAsync();

            // Record usage as telemetry for future analytics and operations.
            if (request.InputTokens.HasValue || request.OutputTokens.HasValue)
                await tokenBudgetService.RecordUsageAsync(
                    userId,
                    session?.Provider ?? string.Empty,
                    session?.Model ?? string.Empty,
                    request.InputTokens ?? 0,
                    request.OutputTokens ?? 0);

            return new ChatMessageDto
            {
                Id = messageEntity.Id,
                SessionId = messageEntity.SessionId,
                Role = messageEntity.Role,
                Content = messageEntity.Content,
                MessageType = messageEntity.MessageType,
                Metadata = messageEntity.Metadata,
                InputTokens = messageEntity.InputTokens,
                OutputTokens = messageEntity.OutputTokens,
                TotalTokens = messageEntity.TotalTokens,
                Model = messageEntity.Model,
                CreatedAt = messageEntity.CreatedAt
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error saving message to session {SessionId}", request.SessionId);
            throw;
        }
    }

    public async Task<List<ChatMessageDto>> SaveMessagesAsync(SaveChatMessagesRequest request, Guid userId)
    {
        try
        {
            // Verify user has access to the session
            var hasAccess = await dataContext.AiChatSessions
                .AnyAsync(s => s.Id == request.SessionId &&
                              s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (!hasAccess)
            {
                logger.Warning("User {UserId} does not have access to session {SessionId}", userId, request.SessionId);
                throw new UnauthorizedAccessException("Access denied to this session");
            }

            var now = DateTime.UtcNow;
            var savedMessages = new List<ChatMessageDto>();
            var messageIndex = 0;

            foreach (var message in request.Messages)
            {
                // Increment timestamp by milliseconds to preserve message order
                var messageTimestamp = now.AddMilliseconds(messageIndex);

                var messageEntity = new AiChatMessage
                {
                    Id = Guid.NewGuid(),
                    SessionId = request.SessionId,
                    Role = message.Role,
                    Content = message.Content,
                    MessageType = message.MessageType,
                    Metadata = message.Metadata,
                    CreatedAt = messageTimestamp
                };

                dataContext.AiChatMessages.Add(messageEntity);
                messageIndex++;

                savedMessages.Add(new ChatMessageDto
                {
                    Id = messageEntity.Id,
                    SessionId = messageEntity.SessionId,
                    Role = messageEntity.Role,
                    Content = messageEntity.Content,
                    MessageType = messageEntity.MessageType,
                    Metadata = messageEntity.Metadata,
                    CreatedAt = messageEntity.CreatedAt
                });
            }

            // Update session's UpdatedAt timestamp
            var session = await dataContext.AiChatSessions.FindAsync(request.SessionId);
            if (session != null)
            {
                session.UpdatedAt = now;
            }

            await dataContext.SaveChangesAsync();

            logger.Debug("Saved {Count} messages to session {SessionId}", request.Messages.Count, request.SessionId);

            return savedMessages;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error saving messages to session {SessionId}", request.SessionId);
            throw;
        }
    }

    public async Task<bool> ClearSessionMessagesAsync(Guid sessionId, Guid userId)
    {
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId &&
                                         s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (session == null)
            {
                logger.Warning("Session {SessionId} not found or user {UserId} does not have access", sessionId, userId);
                return false;
            }

            var messages = await dataContext.AiChatMessages
                .Where(m => m.SessionId == sessionId)
                .ToListAsync();

            dataContext.AiChatMessages.RemoveRange(messages);

            session.UpdatedAt = DateTime.UtcNow;

            await dataContext.SaveChangesAsync();

            logger.Debug("Cleared {Count} messages from session {SessionId}", messages.Count, sessionId);

            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error clearing messages from session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task AccumulateTokensAsync(Guid sessionId, Guid userId, int? inputTokens, int? outputTokens, int? cacheReadTokens = null, int? cacheCreationTokens = null)
    {
        if (!inputTokens.HasValue && !outputTokens.HasValue && !cacheReadTokens.HasValue && !cacheCreationTokens.HasValue) return;
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId &&
                                         s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (session == null) return;

            ApplyTokenUsage(session, inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens);
            session.UpdatedAt = DateTime.UtcNow;
            await dataContext.SaveChangesAsync();

            await tokenBudgetService.RecordUsageAsync(
                userId,
                session.Provider ?? string.Empty,
                session.Model ?? string.Empty,
                inputTokens ?? 0,
                outputTokens ?? 0);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to accumulate tokens for session {SessionId}", sessionId);
        }
    }

    private static void ApplyTokenUsage(
        AiChatSession session,
        int? inputTokens,
        int? outputTokens,
        int? cacheReadTokens,
        int? cacheCreationTokens)
    {
        if (inputTokens.HasValue) session.TotalInputTokens += inputTokens.Value;
        if (outputTokens.HasValue) session.TotalOutputTokens += outputTokens.Value;
        if (inputTokens.HasValue || outputTokens.HasValue)
            session.TotalTokens += (inputTokens ?? 0) + (outputTokens ?? 0);
        if (cacheReadTokens.HasValue) session.TotalCacheReadTokens += cacheReadTokens.Value;
        if (cacheCreationTokens.HasValue) session.TotalCacheCreationTokens += cacheCreationTokens.Value;
    }

    public async Task IncrementApiCallsAsync(Guid sessionId, Guid userId, int count)
    {
        if (count <= 0) return;
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId &&
                                         s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (session == null) return;

            session.TotalApiCalls += count;
            session.UpdatedAt = DateTime.UtcNow;
            await dataContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to increment API calls for session {SessionId}", sessionId);
        }
    }

    public async Task UpdateSessionComplexityTierAsync(Guid sessionId, string tier, Guid userId)
    {
        try
        {
            var session = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId &&
                                         s.AiChatSessionUsers.Any(su => su.UserId == userId));

            if (session == null)
                return;

            session.ComplexityTier = tier;
            session.UpdatedAt = DateTime.UtcNow;
            await dataContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to update complexity tier for session {SessionId}", sessionId);
            // Non-critical — don't rethrow
        }
    }

    public async Task<AppChatStatsDto?> GetAppChatStatsAsync(Guid appId, Guid userId)
    {
        try
        {
            // Get all sessions for this app where user has access
            var sessions = await dataContext.AiChatSessions
                .Where(s => s.ResourceType == ChatResourceType.WebApp &&
                           s.ResourceId == appId &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId))
                .ToListAsync();

            if (sessions.Count == 0)
            {
                return new AppChatStatsDto
                {
                    TotalPromptsSubmitted = 0,
                    TotalApiCalls = 0,
                    TotalTokens = 0,
                    InputTokens = 0,
                    OutputTokens = 0,
                    SessionCount = 0
                };
            }

            // Aggregate stats across all sessions
            var totalInputTokens = sessions.Sum(s => s.TotalInputTokens);
            var totalCacheReadTokens = sessions.Sum(s => s.TotalCacheReadTokens);
            var stats = new AppChatStatsDto
            {
                TotalPromptsSubmitted = sessions.Sum(s => s.TotalPromptsSubmitted),
                TotalApiCalls = sessions.Sum(s => s.TotalApiCalls),
                TotalTokens = sessions.Sum(s => s.TotalTokens),
                InputTokens = totalInputTokens,
                OutputTokens = sessions.Sum(s => s.TotalOutputTokens),
                SessionCount = sessions.Count,
                TotalCacheReadTokens = totalCacheReadTokens,
                TotalCacheCreationTokens = sessions.Sum(s => s.TotalCacheCreationTokens),
                EffectiveInputTokens = Math.Max(0, totalInputTokens - totalCacheReadTokens),
                CacheHitRate = totalInputTokens > 0 ? Math.Min(1.0, (double)totalCacheReadTokens / totalInputTokens) : 0,
                CacheHitPercent = totalInputTokens > 0 ? Math.Min(100.0, ((double)totalCacheReadTokens / totalInputTokens) * 100.0) : 0
            };

            return stats;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting app chat stats for {AppId}", appId);
            throw;
        }
    }

    public async Task<ChatSessionExportDto> ExportResourceSessionsAsync(ChatResourceType resourceType, Guid resourceId, Guid userId)
    {
        try
        {
            var sessionEntities = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .Include(s => s.Messages)
                .Where(s => s.ResourceType == resourceType &&
                           s.ResourceId == resourceId &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId))
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync();

            var sessions = sessionEntities.Select(session => new ChatSessionExportItemDto
            {
                Session = new ChatSessionDto
                {
                    Id = session.Id,
                    ResourceType = session.ResourceType,
                    ResourceId = session.ResourceId,
                    Title = session.Title,
                    Provider = session.Provider,
                    Model = session.Model,
                    IsActive = session.IsActive,
                    CreatedAt = session.CreatedAt,
                    UpdatedAt = session.UpdatedAt,
                    MessageCount = session.Messages.Count,
                    TotalPromptsSubmitted = session.TotalPromptsSubmitted,
                    TotalApiCalls = session.TotalApiCalls,
                    TotalInputTokens = session.TotalInputTokens,
                    TotalOutputTokens = session.TotalOutputTokens,
                    TotalTokens = session.TotalTokens,
                    TotalCacheReadTokens = session.TotalCacheReadTokens,
                    TotalCacheCreationTokens = session.TotalCacheCreationTokens,
                    EffectiveInputTokens = Math.Max(0, session.TotalInputTokens - session.TotalCacheReadTokens),
                    CacheHitRate = session.TotalInputTokens > 0 ? Math.Min(1.0, (double)session.TotalCacheReadTokens / session.TotalInputTokens) : 0,
                    CacheHitPercent = session.TotalInputTokens > 0 ? Math.Min(100.0, ((double)session.TotalCacheReadTokens / session.TotalInputTokens) * 100.0) : 0,
                    ComplexityTier = session.ComplexityTier,
                    EffectiveVerificationStrategy = session.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(VerificationStrategyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(VerificationStrategyPrefix.Length))
                        .FirstOrDefault(),
                    EffectiveExecutionPolicy = session.Messages
                        .Where(m => m.Role == "system" && m.MessageType == ChatMessageType.System && m.Content.StartsWith(ExecutionPolicyPrefix))
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => m.Content.Substring(ExecutionPolicyPrefix.Length))
                        .FirstOrDefault()
                },
                Messages = session.Messages
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new ChatMessageDto
                    {
                        Id = m.Id,
                        SessionId = m.SessionId,
                        Role = m.Role,
                        Content = m.Content,
                        MessageType = m.MessageType,
                        Metadata = m.Metadata,
                        InputTokens = m.InputTokens,
                        OutputTokens = m.OutputTokens,
                        TotalTokens = m.TotalTokens,
                        Model = m.Model,
                        CreatedAt = m.CreatedAt
                    })
                    .ToList()
            }).ToList();

            return new ChatSessionExportDto
            {
                ResourceType = resourceType,
                ResourceId = resourceId,
                ExportedAtUtc = DateTime.UtcNow,
                SessionCount = sessions.Count,
                Sessions = sessions
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error exporting chat sessions for resource {ResourceType} {ResourceId}", resourceType, resourceId);
            throw;
        }
    }

}
