using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Studio.Server.Services;

public class InvitationTokenService
{
    private readonly ApplicationDbContext dbContext;
    private readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

    public InvitationTokenService(ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<string> GenerateInvitationToken(Guid projectId, string email, string role, Guid invitedBy)
    {
        var token = GenerateSecureToken();

        var entity = new InvitationToken
        {
            Id = Guid.NewGuid(),
            Token = token,
            ProjectId = projectId,
            Email = email,
            Role = role,
            InvitedBy = invitedBy,
            ExpiresAt = DateTime.UtcNow.Add(DefaultLifetime),
            IsConsumed = false,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Add(entity);
        await dbContext.SaveChangesAsync();

        return token;
    }

    public async Task<InvitationToken?> ValidateInvitationToken(string token)
    {
        var entity = await dbContext.Set<InvitationToken>()
            .FirstOrDefaultAsync(t => t.Token == token);

        if (entity == null)
            return null;

        if (entity.IsConsumed)
            return null;

        if (entity.ExpiresAt < DateTime.UtcNow)
            return null;

        return entity;
    }

    public async Task<bool> ConsumeInvitationToken(string token)
    {
        var entity = await dbContext.Set<InvitationToken>()
            .FirstOrDefaultAsync(t => t.Token == token);

        if (entity == null)
            return false;

        if (entity.IsConsumed || entity.ExpiresAt < DateTime.UtcNow)
            return false;

        entity.IsConsumed = true;
        await dbContext.SaveChangesAsync();
        return true;
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
    }
}