using System.Net;
using System.Net.Sockets;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public interface ICustomDomainResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class DnsCustomDomainResolver : ICustomDomainResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return [];
        }
    }
}

public sealed record AppCustomDomainDto(
    Guid Id, string Host, bool IsPrimary, bool Verified, DateTimeOffset? VerifiedAt);

/// <param name="Target">The address the domain's CNAME record must point to (an A record may use <paramref name="TargetAddresses"/>).</param>
public sealed record AppCustomDomainsState(
    string Target, IReadOnlyList<string> TargetAddresses, IReadOnlyList<AppCustomDomainDto> Domains);

/// <summary>
/// Adds, verifies and removes the owner's own domains for a public app. A domain starts routing (and gets a certificate
/// from Traefik) only after DNS points at this server; removing it removes the route, so renewal stops.
/// </summary>
public sealed class CustomDomainService(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    IOptions<PublishedAppsConfig> options,
    ICustomDomainResolver resolver,
    BackendRouteConfigService routeConfigService)
{
    private readonly PublishedAppsConfig config = options.Value;

    public async Task<AppCustomDomainsState> GetAsync(Guid appId, CancellationToken cancellationToken)
    {
        await LoadManagedAppAsync(appId, cancellationToken);
        return await BuildStateAsync(appId, cancellationToken);
    }

    public async Task<AppCustomDomainsState> AddAsync(Guid appId, string? host, CancellationToken cancellationToken)
    {
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        if (!AppSharingService.IsOpenToInternet(app))
            throw new InvalidOperationException("Only an app open to anyone on the internet can have its own domain.");

        var normalized = Normalize(host);
        ValidateHost(normalized);
        if (await dbContext.AppCustomDomains.AnyAsync(domain => domain.Host == normalized, cancellationToken))
            throw new InvalidOperationException("This domain is already used by an app.");

        dbContext.AppCustomDomains.Add(new AppCustomDomain { Id = Guid.NewGuid(), WebAppId = appId, Host = normalized });
        await dbContext.SaveChangesAsync(cancellationToken);
        return await BuildStateAsync(appId, cancellationToken);
    }

    /// <summary>Checks the domain's DNS. When it points at this server the domain starts routing.</summary>
    public async Task<AppCustomDomainsState> VerifyAsync(Guid appId, Guid domainId, CancellationToken cancellationToken)
    {
        await LoadManagedAppAsync(appId, cancellationToken);
        var domain = await FindAsync(appId, domainId, cancellationToken);

        var targets = await ResolveTargetAddressesAsync(cancellationToken);
        var actual = await resolver.ResolveAsync(domain.Host, cancellationToken);
        if (targets.Count == 0 || !actual.Any(address => targets.Contains(address)))
            throw new InvalidOperationException(
                $"{domain.Host} does not point at this server yet. Create the DNS record shown, wait for it to spread, and check again.");

        if (domain.VerifiedAt is null)
        {
            domain.VerifiedAt = DateTimeOffset.UtcNow;
            // The first verified domain becomes the app's address; the old address then redirects to it.
            domain.IsPrimary = !await dbContext.AppCustomDomains.AnyAsync(
                other => other.WebAppId == appId && other.IsPrimary, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await routeConfigService.RefreshAsync(cancellationToken);
        }

        return await BuildStateAsync(appId, cancellationToken);
    }

    public async Task<AppCustomDomainsState> SetPrimaryAsync(Guid appId, Guid domainId, CancellationToken cancellationToken)
    {
        await LoadManagedAppAsync(appId, cancellationToken);
        var domain = await FindAsync(appId, domainId, cancellationToken);
        if (domain.VerifiedAt is null)
            throw new InvalidOperationException("Check the domain's DNS before making it the primary address.");

        var domains = await dbContext.AppCustomDomains.Where(other => other.WebAppId == appId).ToListAsync(cancellationToken);
        foreach (var other in domains) other.IsPrimary = other.Id == domainId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await routeConfigService.RefreshAsync(cancellationToken);
        return await BuildStateAsync(appId, cancellationToken);
    }

    public async Task<AppCustomDomainsState> RemoveAsync(Guid appId, Guid domainId, CancellationToken cancellationToken)
    {
        await LoadManagedAppAsync(appId, cancellationToken);
        var domain = await FindAsync(appId, domainId, cancellationToken);
        dbContext.AppCustomDomains.Remove(domain);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Another verified domain takes over as primary when the primary one goes.
        if (domain.IsPrimary)
        {
            var next = await dbContext.AppCustomDomains
                .Where(other => other.WebAppId == appId && other.VerifiedAt != null)
                .OrderBy(other => other.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsPrimary = true;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        await routeConfigService.RefreshAsync(cancellationToken);
        return await BuildStateAsync(appId, cancellationToken);
    }

    public static string Normalize(string? host) => (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

    private void ValidateHost(string host)
    {
        if (host.Length is 0 or > 253 || host.Split('.') is not { Length: >= 2 } labels || labels.Any(label => !IsValidLabel(label)))
            throw new InvalidOperationException("Enter a domain name such as blog.example.com.");
        if (IPAddress.TryParse(host, out _) || labels[^1].All(char.IsDigit))
            throw new InvalidOperationException("Enter a domain name, not an IP address.");

        var appsDomain = config.PublicBaseUrlHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (host == appsDomain || host.EndsWith("." + appsDomain, StringComparison.Ordinal))
            throw new InvalidOperationException("Use a domain outside this server's app addresses.");

        var allowed = config.AllowedCustomDomains
            .Select(Normalize).Where(entry => entry.Length > 0).ToList();
        if (allowed.Count > 0 && !allowed.Any(entry => host == entry || host.EndsWith("." + entry, StringComparison.Ordinal)))
            throw new InvalidOperationException("The administrator allows custom domains only under: " + string.Join(", ", allowed) + ".");
    }

    private static bool IsValidLabel(string label) =>
        label.Length is >= 1 and <= 63 && label[0] != '-' && label[^1] != '-' &&
        label.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private async Task<List<IPAddress>> ResolveTargetAddressesAsync(CancellationToken cancellationToken) =>
        (await resolver.ResolveAsync(config.PublicBaseUrlHost, cancellationToken)).ToList();

    private async Task<AppCustomDomainsState> BuildStateAsync(Guid appId, CancellationToken cancellationToken)
    {
        var domains = await dbContext.AppCustomDomains.AsNoTracking()
            .Where(domain => domain.WebAppId == appId).OrderBy(domain => domain.CreatedAt).ToListAsync(cancellationToken);
        var addresses = await ResolveTargetAddressesAsync(cancellationToken);
        return new AppCustomDomainsState(
            config.PublicBaseUrlHost,
            addresses.Select(address => address.ToString()).ToList(),
            domains.Select(domain => new AppCustomDomainDto(
                domain.Id, domain.Host, domain.IsPrimary, domain.VerifiedAt is not null, domain.VerifiedAt)).ToList());
    }

    private async Task<AppCustomDomain> FindAsync(Guid appId, Guid domainId, CancellationToken cancellationToken) =>
        await dbContext.AppCustomDomains.FirstOrDefaultAsync(domain => domain.Id == domainId && domain.WebAppId == appId, cancellationToken)
        ?? throw new KeyNotFoundException("That domain is not set for this app.");

    private async Task<WebApp> LoadManagedAppAsync(Guid appId, CancellationToken cancellationToken)
    {
        var app = await dbContext.WebApps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appId && a.DeletionClaimId == null, cancellationToken);
        if (app is null) throw new KeyNotFoundException("App not found");
        if (!await authorizationService.CanUpdateAppAsync(appId, cancellationToken))
            throw new UnauthorizedAccessException("Only the people who manage this app can change its domains.");
        return app;
    }
}
