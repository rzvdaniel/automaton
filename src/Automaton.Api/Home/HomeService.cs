using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Home;

/// <summary>Studio Home for one person: their Needs-you cards and the apps they pinned.</summary>
public sealed class HomeService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    HomeNeedsYouService needsYou)
{
    private readonly AppListingService apps = new(db, authorization);


    public async Task<HomeResponse> GetAsync(Guid userId, CancellationToken token)
    {
        var listed = await apps.ListAsync(null, token);
        var pins = await db.UserHomePins.AsNoTracking().Where(pin => pin.UserId == userId)
            .OrderBy(pin => pin.SortOrder).ThenBy(pin => pin.PinnedAtUtc).ToListAsync(token);
        var byId = listed.ToDictionary(app => app.Id);
        // An app the person can no longer see simply drops out of Home.
        var visiblePins = pins.Where(pin => byId.ContainsKey(pin.AppId)).ToList();
        var cards = await LoadAppCardsAsync(visiblePins.Select(pin => pin.AppId).ToList(), token);
        var pinned = visiblePins
            .Select(pin => new HomePinnedApp(byId[pin.AppId], pin.PinnedAtUtc, cards.GetValueOrDefault(pin.AppId))).ToList();

        var needsYouCards = new List<HomeCard>();
        if (await authorization.IsSystemAdministratorAsync(userId, token)) needsYouCards.AddRange(await needsYou.ServerCardsAsync(token));
        needsYouCards.AddRange(HomeNeedsYouService.AppCards(listed));
        var dismissed = await db.UserHomeDismissals.AsNoTracking().Where(item => item.UserId == userId)
            .Select(item => item.CardKey).ToListAsync(token);
        return new(needsYouCards.Where(card => !dismissed.Contains(card.Key)).ToList(), pinned);
    }

    /// <summary>A card is late when its last card run failed, or it was not refreshed within three of its intervals.</summary>
    private async Task<Dictionary<Guid, AppHomeCardDto>> LoadAppCardsAsync(IReadOnlyList<Guid> appIds, CancellationToken token)
    {
        if (appIds.Count == 0) return [];
        var rows = await db.AppHomeCards.AsNoTracking().Where(card => appIds.Contains(card.AppId)).ToListAsync(token);
        if (rows.Count == 0) return [];
        var intervals = await db.ActionSchedules.AsNoTracking()
            .Where(schedule => appIds.Contains(schedule.ApplicationId) && schedule.SystemIdentity != null
                && schedule.SystemIdentity.StartsWith(HomeCardSchedules.IdentityPrefix))
            .Select(schedule => new { schedule.ApplicationId, schedule.IntervalSeconds }).ToListAsync(token);
        var now = DateTime.UtcNow;
        var result = new Dictionary<Guid, AppHomeCardDto>();
        foreach (var row in rows)
        {
            var content = AppHomeCardValidator.Deserialize(row.CardJson);
            if (content is null) continue;
            var interval = intervals.FirstOrDefault(item => item.ApplicationId == row.AppId)?.IntervalSeconds;
            var stale = row.LastFailedAtUtc > row.UpdatedAtUtc
                || interval is { } seconds && now - row.UpdatedAtUtc > TimeSpan.FromSeconds(seconds * 3L);
            result[row.AppId] = new(content, row.UpdatedAtUtc, stale);
        }
        return result;
    }

    /// <summary>Pins an app from the person's Apps list. Returns false when it is not on that list.</summary>
    public async Task<bool> PinAsync(Guid userId, Guid appId, CancellationToken token)
    {
        // Only apps on the person's own Apps list can be pinned, so a pin always shows up on Home.
        if (!await authorization.GetListedAppsQuery(null).AnyAsync(app => app.Id == appId, token)) return false;
        if (await db.UserHomePins.AnyAsync(pin => pin.UserId == userId && pin.AppId == appId, token)) return true;
        var last = await db.UserHomePins.Where(pin => pin.UserId == userId).Select(pin => (int?)pin.SortOrder).MaxAsync(token);
        db.UserHomePins.Add(new UserHomePin { UserId = userId, AppId = appId, PinnedAtUtc = DateTime.UtcNow, SortOrder = (last ?? -1) + 1 });
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            // A parallel request pinned it first, or the app was just deleted.
            db.ChangeTracker.Clear();
            return await db.UserHomePins.AsNoTracking().AnyAsync(pin => pin.UserId == userId && pin.AppId == appId, token);
        }
        return true;
    }

    public async Task UnpinAsync(Guid userId, Guid appId, CancellationToken token)
    {
        var pin = await db.UserHomePins.SingleOrDefaultAsync(item => item.UserId == userId && item.AppId == appId, token);
        if (pin is null) return;
        db.UserHomePins.Remove(pin);
        await db.SaveChangesAsync(token);
    }

    /// <summary>Stores the order of the person's pins. Returns false unless the ids are exactly their pinned apps.</summary>
    public async Task<bool> ReorderAsync(Guid userId, IReadOnlyList<Guid> appIds, CancellationToken token)
    {
        var pins = await db.UserHomePins.Where(pin => pin.UserId == userId).ToListAsync(token);
        if (appIds.Count != pins.Count || appIds.Distinct().Count() != appIds.Count
            || !pins.Select(pin => pin.AppId).ToHashSet().SetEquals(appIds)) return false;
        for (var index = 0; index < appIds.Count; index++) pins.Single(pin => pin.AppId == appIds[index]).SortOrder = index;
        await db.SaveChangesAsync(token);
        return true;
    }

    /// <summary>Dismisses an optional card. Returns false for a card that cannot be dismissed.</summary>
    public async Task<bool> DismissAsync(Guid userId, string cardKey, CancellationToken token)
    {
        if (!HomeCardKeys.Dismissible.Contains(cardKey)) return false;
        if (await db.UserHomeDismissals.AnyAsync(item => item.UserId == userId && item.CardKey == cardKey, token)) return true;
        db.UserHomeDismissals.Add(new UserHomeDismissal { UserId = userId, CardKey = cardKey, DismissedAtUtc = DateTime.UtcNow });
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.UserHomeDismissals.AsNoTracking().AnyAsync(item => item.UserId == userId && item.CardKey == cardKey, token);
        }
        return true;
    }
}
