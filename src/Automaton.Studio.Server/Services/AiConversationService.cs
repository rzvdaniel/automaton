using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class AiConversationService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly ILogger<AiConversationService> logger;

    public AiConversationService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        ILogger<AiConversationService> logger)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.logger = logger;
    }

    public async Task<AiConversation> CreateConversationAsync(string title, string language = "Python", string? description = null)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            throw new UnauthorizedAccessException("User must be authenticated to create conversations");

        var conversation = new AiConversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Description = description,
            Language = language,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.AiConversations.Add(conversation);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Created AI conversation {ConversationId} for user {UserId}", conversation.Id, userId);
        return conversation;
    }

    public async Task<List<AiConversation>> GetUserConversationsAsync()
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            return new List<AiConversation>();

        return await dbContext.AiConversations
            .Where(c => c.UserId == userId && c.IsActive)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<AiConversation?> GetConversationAsync(Guid conversationId)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            return null;

        return await dbContext.AiConversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.IsActive);
    }

    public async Task<AiConversationMessage> AddMessageAsync(Guid conversationId, MessageRole role, string content, 
        string? generatedCode = null, bool hasError = false, string? errorMessage = null, 
        AiProvider? provider = null, string? model = null)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            throw new UnauthorizedAccessException("User must be authenticated to add messages");

        // Verify conversation ownership
        var conversation = await dbContext.AiConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.IsActive);
        
        if (conversation == null)
            throw new ArgumentException("Conversation not found or access denied");

        var message = new AiConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            GeneratedCode = generatedCode,
            HasError = hasError,
            ErrorMessage = errorMessage,
            Provider = provider,
            Model = model,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiConversationMessages.Add(message);
        
        // Update conversation timestamp
        conversation.UpdatedAt = DateTime.UtcNow;
        
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Added message to conversation {ConversationId} by user {UserId}", conversationId, userId);
        return message;
    }

    public async Task<List<AiConversationMessage>> GetConversationMessagesAsync(Guid conversationId)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            return new List<AiConversationMessage>();

        // Verify conversation ownership
        var hasAccess = await dbContext.AiConversations
            .AnyAsync(c => c.Id == conversationId && c.UserId == userId && c.IsActive);
        
        if (!hasAccess)
            return new List<AiConversationMessage>();

        return await dbContext.AiConversationMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> DeleteConversationAsync(Guid conversationId)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            return false;

        var conversation = await dbContext.AiConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

        if (conversation == null)
            return false;

        // Soft delete
        conversation.IsActive = false;
        conversation.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        logger.LogInformation("Deleted conversation {ConversationId} for user {UserId}", conversationId, userId);
        return true;
    }

    public async Task<bool> UpdateConversationTitleAsync(Guid conversationId, string title)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
            return false;

        var conversation = await dbContext.AiConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.IsActive);

        if (conversation == null)
            return false;

        conversation.Title = title;
        conversation.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        return true;
    }
}