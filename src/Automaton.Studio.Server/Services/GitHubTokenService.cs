using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services.Interfaces;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing GitHub Personal Access Tokens per user
/// </summary>
public class GitHubTokenService
{
    private readonly ApplicationDbContext dataContext;
    private readonly IDataProtector dataProtector;
    private readonly IAuditEventService auditEventService;
    private readonly Serilog.ILogger logger;

    public GitHubTokenService(
        ApplicationDbContext dataContext,
        IDataProtectionProvider dataProtectionProvider,
        IAuditEventService auditEventService)
    {
        this.dataContext = dataContext;
        this.dataProtector = dataProtectionProvider.CreateProtector("GitHubTokens");
        this.auditEventService = auditEventService;
        this.logger = Serilog.Log.ForContext<GitHubTokenService>();
    }

    /// <summary>
    /// Store or update a GitHub token for a user
    /// </summary>
    public async Task SetTokenForUserAsync(Guid userId, string token, string? username = null)
    {
        var encryptedToken = dataProtector.Protect(token);

        var existing = await dataContext.UserGitHubTokens
            .FirstOrDefaultAsync(t => t.UserId == userId);

        if (existing != null)
        {
            existing.EncryptedToken = encryptedToken;
            existing.GitHubUsername = username;
            existing.LastValidatedAt = DateTime.UtcNow;
            existing.IsValid = true;
        }
        else
        {
            var newToken = new UserGitHubToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EncryptedToken = encryptedToken,
                GitHubUsername = username,
                CreatedAt = DateTime.UtcNow,
                LastValidatedAt = DateTime.UtcNow,
                IsValid = true
            };
            dataContext.UserGitHubTokens.Add(newToken);
        }

        await dataContext.SaveChangesAsync();
        await auditEventService.WriteAsync(
            action: "Set",
            entityType: "GitHubToken",
            entityId: userId,
            message: "GitHub token was configured",
            metadata: new { UserId = userId, GitHubUsername = username },
            failOnError: true);

    }

    /// <summary>
    /// Get the decrypted GitHub token for a user
    /// </summary>
    public async Task<string?> GetTokenForUserAsync(Guid userId)
    {
        var tokenEntity = await dataContext.UserGitHubTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IsValid);

        if (tokenEntity == null)
        {
            return null;
        }

        try
        {
            return dataProtector.Unprotect(tokenEntity.EncryptedToken);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to decrypt GitHub token for user {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Check if a user has a valid GitHub token configured
    /// </summary>
    public async Task<bool> HasTokenAsync(Guid userId)
    {
        return await dataContext.UserGitHubTokens
            .AnyAsync(t => t.UserId == userId && t.IsValid);
    }

    /// <summary>
    /// Get GitHub username associated with a user's token
    /// </summary>
    public async Task<string?> GetUsernameAsync(Guid userId)
    {
        var tokenEntity = await dataContext.UserGitHubTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IsValid);

        return tokenEntity?.GitHubUsername;
    }

    /// <summary>
    /// Remove a user's GitHub token
    /// </summary>
    public async Task RemoveTokenAsync(Guid userId)
    {
        var token = await dataContext.UserGitHubTokens
            .FirstOrDefaultAsync(t => t.UserId == userId);

        if (token != null)
        {
            dataContext.UserGitHubTokens.Remove(token);
            await dataContext.SaveChangesAsync();
            await auditEventService.WriteAsync(
                action: "Removed",
                entityType: "GitHubToken",
                entityId: userId,
                message: "GitHub token was removed",
                metadata: new { UserId = userId },
                failOnError: true);

        }
    }

    /// <summary>
    /// Mark a token as invalid (after failed authentication, etc.)
    /// </summary>
    public async Task MarkTokenInvalidAsync(Guid userId)
    {
        var token = await dataContext.UserGitHubTokens
            .FirstOrDefaultAsync(t => t.UserId == userId);

        if (token != null)
        {
            token.IsValid = false;
            await dataContext.SaveChangesAsync();
            logger.Warning("GitHub token marked as invalid for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Validate a token by attempting to use it (updates LastValidatedAt)
    /// </summary>
    public async Task<bool> ValidateTokenAsync(Guid userId)
    {
        var token = await GetTokenForUserAsync(userId);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            var client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("Automaton"))
            {
                Credentials = new Octokit.Credentials(token)
            };

            var user = await client.User.Current();
            
            var tokenEntity = await dataContext.UserGitHubTokens
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (tokenEntity != null)
            {
                tokenEntity.LastValidatedAt = DateTime.UtcNow;
                tokenEntity.IsValid = true;
                tokenEntity.GitHubUsername = user.Login;
                await dataContext.SaveChangesAsync();
            }

            logger.Debug("GitHub token validated for user {UserId} (GitHub: {GitHubUser})", userId, user.Login);
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to validate GitHub token for user {UserId}", userId);
            await MarkTokenInvalidAsync(userId);
            return false;
        }
    }
}
