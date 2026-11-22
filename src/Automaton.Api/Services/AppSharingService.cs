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
    public const string Team = "team";
    public const string Server = "server";
    public const string Internet = "internet";

    public static bool IsValid(string? audience) => audience is OnlyMe or People or Team or Server or Internet;
}

public sealed record AppSharingPerson(Guid UserId, string Email, string DisplayName);

public sealed record AppSharingTeam(Guid Id, string Name);

/// <param name="Team">The team whose members open the app (audience "team").</param>
/// <param name="Listed">For "internet": whether the app is listed in the marketplace.</param>
public sealed record AppSharingState(
    string Audience, IReadOnlyList<AppSharingPerson> People, string? PublicUrlSlug,
    AppSharingTeam? Team = null, bool Listed = false);

/// <summary>
/// One answer to "who can open this app?": only the owner, chosen people, a team, everyone signed in on this server,
/// or anyone on the internet (optionally listed in the marketplace). Studio, the API and the CLI all go through this service.
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
        Guid appId, string audience, string? publicUrlSlug, Guid? teamId, bool? listed, CancellationToken cancellationToken)
    {
        if (!AppSharingAudiences.IsValid(audience))
            throw new InvalidOperationException("Choose one of: me, people, team, server, internet.");
        if (listed == true && audience != AppSharingAudiences.Internet)
            throw new InvalidOperationException("Only an app open to anyone on the internet can be listed in the store.");
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        var team = audience == AppSharingAudiences.Team ? await LoadOwnTeamAsync(teamId, cancellationToken) : null;

        if (audience == AppSharingAudiences.Internet)
        {
            var slug = string.IsNullOrWhiteSpace(publicUrlSlug) ? app.PublicUrl : publicUrlSlug.Trim();
            if (string.IsNullOrWhiteSpace(slug))
                throw new InvalidOperationException("Anyone on the internet needs a public address (publicUrl).");
            await publicationService.Publish(
                appId, slug, requiresAuthentication: false, executionRunnerId: null, category: app.Category,
                tags: app.Tags, isForkable: app.IsForkable,
                catalogVisibility: listed switch
                {
                    true => AppCatalogVisibilities.Listed,
                    false => AppCatalogVisibilities.Unlisted,
                    null => app.CatalogVisibility
                },
                cancellationToken: cancellationToken);
            app.SharedWithServer = false;
        }
        else
        {
            if (app.IsPublished) await publicationService.Unpublish(appId, cancellationToken);
            app.SharedWithServer = audience == AppSharingAudiences.Server;
        }

        // Chosen people only count while the audience is "people"; any other choice forgets them.
        if (audience != AppSharingAudiences.People)
            dbContext.AppShares.RemoveRange(await dbContext.AppShares.Where(share => share.WebAppId == appId).ToListAsync(cancellationToken));

        app.SharedWithProjectId = team?.Id;
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

        // Chosen people only count for "people": narrow a team or server share, and never leave the internet silently.
        if (IsOpenToInternet(app))
            throw new InvalidOperationException("This app is open to anyone on the internet. Choose 'people' first.");
        app.SharedWithServer = false;
        app.SharedWithProjectId = null;

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

    // A team can be chosen only by one of its own members.
    private async Task<Project> LoadOwnTeamAsync(Guid? teamId, CancellationToken cancellationToken)
    {
        if (teamId is null)
            throw new InvalidOperationException("Choose a team (teamId).");
        var team = await dbContext.Projects.FirstOrDefaultAsync(project => project.Id == teamId && project.IsActive &&
            project.Members.Any(member => member.UserId == UserId && member.IsActive), cancellationToken);
        return team ?? throw new KeyNotFoundException("You are not a member of that team.");
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
        var team = app.SharedWithProjectId is null ? null
            : await dbContext.Projects.Where(project => project.Id == app.SharedWithProjectId && project.IsActive)
                .Select(project => new AppSharingTeam(project.Id, project.DisplayName)).FirstOrDefaultAsync(cancellationToken);
        var internet = IsOpenToInternet(app);
        var audience = internet ? AppSharingAudiences.Internet
            : team is not null ? AppSharingAudiences.Team
            : app.SharedWithServer ? AppSharingAudiences.Server
            : people.Count > 0 ? AppSharingAudiences.People
            : AppSharingAudiences.OnlyMe;
        return new AppSharingState(audience, people, string.IsNullOrWhiteSpace(app.PublicUrl) ? null : app.PublicUrl,
            team, internet && app.CatalogVisibility == AppCatalogVisibilities.Listed);
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
