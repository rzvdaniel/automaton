using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public static class AppSharingAudiences
{
    public const string OnlyMe = "me";
    public const string People = "people";
    public const string Server = "server";
    public const string Internet = "internet";

    public static bool IsValid(string? audience) => audience is OnlyMe or People or Server or Internet;
}

public sealed record AppSharingPerson(Guid UserId, string Email, string DisplayName);

public sealed record AppSharingState(string Audience, IReadOnlyList<AppSharingPerson> People, string? PublicUrlSlug);

/// <summary>
/// One answer to "who can open this app?": only the owner, chosen people, everyone signed in on this server,
/// or anyone on the internet. Studio, the API and the CLI all go through this service.
/// </summary>
public sealed class AppSharingService(
    ApplicationDbContext dbContext,
    UserContextService userContextService,
    ResourceAuthorizationService authorizationService,
    AppPublicationService publicationService,
    IAuditEventService auditEventService)
{
    private Guid UserId => userContextService.GetUserId();

    public async Task<AppSharingState> GetAsync(Guid appId, CancellationToken cancellationToken)
    {
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        return await BuildStateAsync(app, cancellationToken);
    }

    public async Task<AppSharingState> SetAudienceAsync(
        Guid appId, string audience, string? publicUrlSlug, CancellationToken cancellationToken)
    {
        if (!AppSharingAudiences.IsValid(audience))
            throw new InvalidOperationException("Choose one of: me, people, server, internet.");
        var app = await LoadManagedAppAsync(appId, cancellationToken);

        if (audience == AppSharingAudiences.Internet)
        {
            var slug = string.IsNullOrWhiteSpace(publicUrlSlug) ? app.PublicUrl : publicUrlSlug.Trim();
            if (string.IsNullOrWhiteSpace(slug))
                throw new InvalidOperationException("Anyone on the internet needs a public address (publicUrl).");
            await publicationService.Publish(
                appId, slug, requiresAuthentication: false, executionRunnerId: null, category: app.Category,
                tags: app.Tags, isForkable: app.IsForkable,
                catalogVisibility: app.CatalogVisibility, cancellationToken: cancellationToken);
            app.SharedWithServer = false;
        }
        else
        {
            if (app.IsPublished) await publicationService.Unpublish(appId, cancellationToken);
            app.SharedWithServer = audience == AppSharingAudiences.Server;
            if (audience == AppSharingAudiences.OnlyMe)
                dbContext.AppShares.RemoveRange(await dbContext.AppShares.Where(share => share.WebAppId == appId).ToListAsync(cancellationToken));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync(appId, $"Sharing set to '{audience}'", cancellationToken);
        return await BuildStateAsync(app, cancellationToken);
    }

    public async Task<AppSharingState> AddPersonAsync(Guid appId, string email, CancellationToken cancellationToken)
    {
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        var normalized = (email ?? string.Empty).Trim().ToUpperInvariant();
        var person = normalized.Length == 0 ? null
            : await dbContext.Users.FirstOrDefaultAsync(user => user.NormalizedEmail == normalized, cancellationToken);
        if (person is null)
            throw new KeyNotFoundException("Nobody on this server uses that email address.");
        if (person.Id == UserId)
            throw new InvalidOperationException("You already have this app.");

        if (!await dbContext.AppShares.AnyAsync(s => s.WebAppId == appId && s.UserId == person.Id, cancellationToken))
        {
            dbContext.AppShares.Add(new AppShare { WebAppId = appId, UserId = person.Id });
            await dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(appId, "Shared with a person", cancellationToken);
        }

        return await BuildStateAsync(app, cancellationToken);
    }

    public async Task<AppSharingState> RemovePersonAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        var share = await dbContext.AppShares
            .FirstOrDefaultAsync(s => s.WebAppId == appId && s.UserId == userId, cancellationToken);
        if (share is null) throw new KeyNotFoundException("That person does not have this app shared.");
        dbContext.AppShares.Remove(share);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync(appId, "Stopped sharing with a person", cancellationToken);
        return await BuildStateAsync(app, cancellationToken);
    }

    private async Task<WebApp> LoadManagedAppAsync(Guid appId, CancellationToken cancellationToken)
    {
        var app = await dbContext.WebApps.FirstOrDefaultAsync(a => a.Id == appId && a.DeletionClaimId == null, cancellationToken);
        if (app is null) throw new KeyNotFoundException("App not found");
        // Only people who can change the app decide who else opens it; shared people cannot re-share.
        if (!await authorizationService.CanUpdateAppAsync(appId, cancellationToken))
            throw new UnauthorizedAccessException("Only the people who manage this app can change who opens it.");
        return app;
    }

    private async Task<AppSharingState> BuildStateAsync(WebApp app, CancellationToken cancellationToken)
    {
        var people = await (from share in dbContext.AppShares
                            join user in dbContext.Users on share.UserId equals user.Id
                            where share.WebAppId == app.Id
                            orderby user.Email
                            select new AppSharingPerson(user.Id, user.Email ?? string.Empty, user.DisplayName ?? string.Empty))
            .ToListAsync(cancellationToken);
        var audience = IsOpenToInternet(app) ? AppSharingAudiences.Internet
            : app.SharedWithServer ? AppSharingAudiences.Server
            : people.Count > 0 ? AppSharingAudiences.People
            : AppSharingAudiences.OnlyMe;
        return new AppSharingState(audience, people, string.IsNullOrWhiteSpace(app.PublicUrl) ? null : app.PublicUrl);
    }

    public static bool IsOpenToInternet(WebApp app) =>
        app.IsPublished && !app.RequiresAuthentication && AppVisibilityPolicy.IsPubliclyAccessible(app);

    private Task AuditAsync(Guid appId, string message, CancellationToken cancellationToken) =>
        auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Changed,
            EntityType = AuditEntityTypes.App,
            EntityId = appId,
            EventType = AuditEventTypes.AppSharingChanged,
            Message = message,
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new { AppId = appId }
        });
}
