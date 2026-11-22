using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DnsClient;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public interface ICustomDomainLiveProbe
{
    /// <summary>True when https://host answers with a valid certificate and the app (not a missing route) responds.</summary>
    Task<bool> IsServingAsync(string host, CancellationToken cancellationToken);
}

public sealed class HttpsCustomDomainLiveProbe : ICustomDomainLiveProbe
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<bool> IsServingAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync($"https://{host}/", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            return status != 404 && status < 500;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
}

public interface ICustomDomainResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ResolveTxtAsync(string host, CancellationToken cancellationToken);
}

public sealed class DnsCustomDomainResolver : ICustomDomainResolver
{
    private static readonly LookupClient Client = new(new LookupClientOptions { UseCache = false, Timeout = TimeSpan.FromSeconds(5), Retries = 1 });

    public async Task<IReadOnlyList<string>> ResolveTxtAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Client.QueryAsync(host, QueryType.TXT, cancellationToken: cancellationToken);
            return response.Answers.TxtRecords().SelectMany(record => record.Text).ToList();
        }
        catch (DnsResponseException)
        {
            return [];
        }
    }

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

/// <param name="ChallengeHost">The TXT record name that proves ownership; its value is <paramref name="ChallengeValue"/>.</param>
public sealed record AppCustomDomainDto(
    Guid Id, string Host, bool IsPrimary, bool Verified, DateTimeOffset? VerifiedAt,
    bool Live, string Status, string ChallengeHost, string ChallengeValue);

/// <param name="Target">The address the domain's CNAME record must point to (an A record may use <paramref name="TargetAddresses"/>).</param>
/// <param name="Check">What the last "Check DNS" found, step by step; null when the state was not the result of a check.</param>
public sealed record AppCustomDomainsState(
    string Target, IReadOnlyList<string> TargetAddresses, IReadOnlyList<AppCustomDomainDto> Domains,
    IReadOnlyList<DomainCheckStep>? Check = null);

/// <param name="Status">"ok", "failed" or "skipped" (not checked because an earlier step failed).</param>
public sealed record DomainCheckStep(string Key, string Title, string Status, string Detail);

/// <summary>A DNS check that did not pass; the message is for people and <see cref="Steps"/> says which step failed.</summary>
public sealed class DomainCheckException(string message, IReadOnlyList<DomainCheckStep> steps) : InvalidOperationException(message)
{
    public IReadOnlyList<DomainCheckStep> Steps { get; } = steps;
}

/// <summary>
/// Adds, verifies and removes the owner's own domains for a public app. A domain starts routing (and gets a certificate
/// from Traefik) only after DNS points at this server; removing it removes the route, so renewal stops.
/// </summary>
public sealed class CustomDomainService(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    IOptions<PublishedAppsConfig> options,
    ICustomDomainResolver resolver,
    ICustomDomainLiveProbe liveProbe,
    BackendRouteConfigService routeConfigService,
    IAppRuntimeRestarter? runtimes = null)
{
    private readonly PublishedAppsConfig config = options.Value;

    private const string ChallengePrefix = "_automaton-challenge.";
    private const string ChallengeValuePrefix = "automaton-verify=";

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

        dbContext.AppCustomDomains.Add(new AppCustomDomain
        {
            Id = Guid.NewGuid(), WebAppId = appId, Host = normalized,
            VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return await BuildStateAsync(appId, cancellationToken);
    }

    /// <summary>Checks the domain's DNS. When it points at this server the domain starts routing.</summary>
    public async Task<AppCustomDomainsState> VerifyAsync(Guid appId, Guid domainId, CancellationToken cancellationToken)
    {
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        var domain = await FindAsync(appId, domainId, cancellationToken);

        // A claim on a name is proven by a TXT record only the name's DNS owner can create; pointing DNS here is not enough.
        var txtName = ChallengePrefix + domain.Host;
        var txtValue = ChallengeValuePrefix + domain.VerificationToken;
        const string TxtTitle = "Ownership (TXT record)", PointsTitle = "Points to this server", CertTitle = "Certificate";
        var proof = await resolver.ResolveTxtAsync(txtName, cancellationToken);
        if (!proof.Any(text => string.Equals(text.Trim(), txtValue, StringComparison.Ordinal)))
            throw new DomainCheckException(
                $"The TXT record {txtName} with the value {txtValue} was not found. Create it, wait for it to spread, and check again.",
                [new("txt", TxtTitle, "failed", $"No TXT record {txtName} with the expected value yet."),
                 new("points", PointsTitle, "skipped", "Checked after the ownership record is found."),
                 new("certificate", CertTitle, "skipped", "Issued once the domain points here.")]);
        var txtOk = new DomainCheckStep("txt", TxtTitle, "ok", "Found the expected value.");

        var targets = await ResolveTargetAddressesAsync(cancellationToken);
        var actual = await resolver.ResolveAsync(domain.Host, cancellationToken);
        if (targets.Count == 0 || !actual.Any(address => targets.Contains(address)))
            throw new DomainCheckException(
                $"{domain.Host} does not point at this server yet. Create the DNS record shown, wait for it to spread, and check again.",
                [txtOk,
                 new("points", PointsTitle, "failed", actual.Count == 0
                     ? $"No CNAME or A record for {domain.Host} yet."
                     : $"{domain.Host} resolves to {string.Join(", ", actual)}, not to this server."),
                 new("certificate", CertTitle, "skipped", "Issued once the domain points here.")]);

        if (domain.VerifiedAt is null)
        {
            domain.VerifiedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            // It routes now so it can get its certificate; it becomes the app's address only once it is live.
            await routeConfigService.RefreshAsync(cancellationToken);
        }

        await CheckLiveAsync(app, domain, cancellationToken);

        var state = await BuildStateAsync(appId, cancellationToken);
        return state with
        {
            Check = [txtOk,
                new("points", PointsTitle, "ok", $"{domain.Host} resolves to {string.Join(", ", actual.Where(targets.Contains))}."),
                domain.LiveAt is not null
                    ? new("certificate", CertTitle, "ok", "Issued; the app answers on the domain.")
                    : new("certificate", CertTitle, "skipped", "Being issued. The domain goes live once the app answers over HTTPS; check again in a minute.")]
        };
    }

    /// <summary>
    /// Probes a verified domain. A domain that serves the app becomes live (and the app's address when it has none);
    /// one that stops serving loses live and primary, and another live domain takes over. Returns true when anything changed.
    /// </summary>
    public async Task<bool> CheckLiveAsync(WebApp app, AppCustomDomain domain, CancellationToken cancellationToken)
    {
        if (domain.VerifiedAt is null) return false;
        var serving = await liveProbe.IsServingAsync(domain.Host, cancellationToken);
        if (serving) ProbeFailures.TryRemove(domain.Id, out _);
        else if (domain.LiveAt is not null && ProbeFailures.AddOrUpdate(domain.Id, 1, (_, count) => count + 1) < FailuresBeforeDrop)
            return false; // a live domain survives a blip (a deploy, a timeout); it drops only after repeated failures
        if (serving == (domain.LiveAt is not null)) return false;

        var primaryBefore = await PrimaryHostAsync(app.Id, cancellationToken);
        domain.LiveAt = serving ? DateTimeOffset.UtcNow : null;
        await UpdatePrimaryAsync(app.Id, domain, serving, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await routeConfigService.RefreshAsync(cancellationToken);
        if (await PrimaryHostAsync(app.Id, cancellationToken) != primaryBefore)
            await RestartForNewAddressAsync(app, CancellationToken.None);
        return true;
    }

    private async Task UpdatePrimaryAsync(Guid appId, AppCustomDomain domain, bool serving, CancellationToken cancellationToken)
    {
        if (serving)
        {
            domain.IsPrimary = !await dbContext.AppCustomDomains.AnyAsync(
                other => other.WebAppId == appId && other.IsPrimary && other.Id != domain.Id, cancellationToken);
            return;
        }

        var wasPrimary = domain.IsPrimary;
        domain.IsPrimary = false;
        if (!wasPrimary) return;
        var next = await dbContext.AppCustomDomains
            .Where(other => other.WebAppId == appId && other.Id != domain.Id && other.LiveAt != null)
            .OrderBy(other => other.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (next is not null) next.IsPrimary = true;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> ProbeFailures = new();
    private const int FailuresBeforeDrop = 3;

    private Task<string?> PrimaryHostAsync(Guid appId, CancellationToken cancellationToken) =>
        dbContext.AppCustomDomains.AsNoTracking().Where(d => d.WebAppId == appId && d.IsPrimary && d.LiveAt != null)
            .Select(d => d.Host).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Re-checks every verified domain of every app; run by the background check.</summary>
    public async Task CheckAllLiveAsync(CancellationToken cancellationToken)
    {
        var domains = await dbContext.AppCustomDomains.Where(domain => domain.VerifiedAt != null).ToListAsync(cancellationToken);
        foreach (var domain in domains)
        {
            var app = await dbContext.WebApps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == domain.WebAppId && a.DeletionClaimId == null, cancellationToken);
            if (app is null) continue;
            try { await CheckLiveAsync(app, domain, cancellationToken); }
            catch (DbUpdateException) { dbContext.ChangeTracker.Clear(); } // removed or changed meanwhile; the next pass retries
        }
    }

    public async Task<AppCustomDomainsState> SetPrimaryAsync(Guid appId, Guid domainId, CancellationToken cancellationToken)
    {
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        var domain = await FindAsync(appId, domainId, cancellationToken);
        if (domain.LiveAt is null)
            throw new InvalidOperationException("The domain can become the primary address once it is live: DNS checked, certificate issued and the app answering there.");

        var domains = await dbContext.AppCustomDomains.Where(other => other.WebAppId == appId).ToListAsync(cancellationToken);
        foreach (var other in domains) other.IsPrimary = other.Id == domainId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await routeConfigService.RefreshAsync(cancellationToken);
        await RestartForNewAddressAsync(app, cancellationToken);
        return await BuildStateAsync(appId, cancellationToken);
    }

    public async Task<AppCustomDomainsState> RemoveAsync(Guid appId, Guid domainId, CancellationToken cancellationToken)
    {
        var app = await LoadManagedAppAsync(appId, cancellationToken);
        var domain = await FindAsync(appId, domainId, cancellationToken);
        dbContext.AppCustomDomains.Remove(domain);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Another verified domain takes over as primary when the primary one goes.
        if (domain.IsPrimary)
        {
            var next = await dbContext.AppCustomDomains
                .Where(other => other.WebAppId == appId && other.LiveAt != null)
                .OrderBy(other => other.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsPrimary = true;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        await routeConfigService.RefreshAsync(cancellationToken);
        if (domain.IsPrimary) await RestartForNewAddressAsync(app, cancellationToken);
        return await BuildStateAsync(appId, cancellationToken);
    }

    /// <summary>An app whose settings default to its address gets the new address only after a restart.</summary>
    private async Task RestartForNewAddressAsync(WebApp app, CancellationToken cancellationToken)
    {
        if (runtimes is not null && AppSecretService.UsesAppAddress(app.ApplicationContractJson))
            await runtimes.RestartRunningAsync(app.Id, CancellationToken.None);
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
                domain.Id, domain.Host, domain.IsPrimary, domain.VerifiedAt is not null, domain.VerifiedAt,
                domain.LiveAt is not null, StatusOf(domain),
                ChallengePrefix + domain.Host, ChallengeValuePrefix + domain.VerificationToken)).ToList());
    }

    private static string StatusOf(AppCustomDomain domain) =>
        domain.VerifiedAt is null ? "WaitingForDns" : domain.LiveAt is null ? "Verified" : "Live";

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
