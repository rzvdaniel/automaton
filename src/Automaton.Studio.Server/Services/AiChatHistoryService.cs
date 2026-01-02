using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class AiChatHistoryService : IAiChatHistoryService
{
    private readonly ApplicationDbContext dataContext;
    private readonly Serilog.ILogger logger;

    public AiChatHistoryService(ApplicationDbContext dataContext)
    {
        this.dataContext = dataContext;
        this.logger = Serilog.Log.ForContext<AiChatHistoryService>();
    }

    public async Task<List<ChatSessionDto>> GetSessionsAsync(ChatResourceType resourceType, Guid resourceId, Guid userId)
    {
        try
        {
            var sessions = await dataContext.AiChatSessions
                .Include(s => s.AiChatSessionUsers)
                .Include(s => s.Messages)
                .Where(s => s.ResourceType == resourceType &&
                           s.ResourceId == resourceId &&
                           s.AiChatSessionUsers.Any(su => su.UserId == userId))
                .OrderByDescending(s => s.UpdatedAt)
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
                    MessageCount = s.Messages.Count
                })
                .ToListAsync();

            return sessions;
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
                    MessageCount = s.Messages.Count
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
                    MessageCount = s.Messages.Count
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

            logger.Information("Created new chat session {SessionId} for {ResourceType} {ResourceId}",
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
                MessageCount = 0
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

            logger.Information("Set session {SessionId} as active", sessionId);

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

            logger.Information("Updated session {SessionId} title to {Title}", sessionId, title);

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

            logger.Information("Deleted session {SessionId}", sessionId);

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

            var messageEntity = new AiChatMessage
            {
                Id = Guid.NewGuid(),
                SessionId = request.SessionId,
                Role = request.Role,
                Content = request.Content,
                MessageType = request.MessageType,
                Metadata = request.Metadata,
                CreatedAt = DateTime.UtcNow
            };

            dataContext.AiChatMessages.Add(messageEntity);

            // Update session's UpdatedAt timestamp
            var session = await dataContext.AiChatSessions.FindAsync(request.SessionId);
            if (session != null)
            {
                session.UpdatedAt = DateTime.UtcNow;
            }

            await dataContext.SaveChangesAsync();

            return new ChatMessageDto
            {
                Id = messageEntity.Id,
                SessionId = messageEntity.SessionId,
                Role = messageEntity.Role,
                Content = messageEntity.Content,
                MessageType = messageEntity.MessageType,
                Metadata = messageEntity.Metadata,
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

            logger.Information("Saved {Count} messages to session {SessionId}", request.Messages.Count, request.SessionId);

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

            logger.Information("Cleared {Count} messages from session {SessionId}", messages.Count, sessionId);

            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error clearing messages from session {SessionId}", sessionId);
            throw;
        }
    }
}
