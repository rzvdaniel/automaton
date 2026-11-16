using System.Net;
using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using static Automaton.Api.Tests.BackendRouteConfigTestFixture;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class CustomDomainTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly IPAddress ServerIp = IPAddress.Parse("203.0.113.10");

    private sealed class FakeResolver : ICustomDomainResolver
    {
        public Dictionary<string, IPAddress[]> Records { get; } = new() { ["localhost"] = [ServerIp] };

        public Dictionary<string, string[]> Txt { get; } = new();

        public Task<IReadOnlyList<string>> ResolveTxtAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Txt.TryGetValue(host, out var found) ? found : []);

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(Records.TryGetValue(host, out var found) ? found : []);
    }

    private sealed class FakeProbe : ICustomDomainLiveProbe
    {
        public bool Serving { get; set; } = true;

        public Task<bool> IsServingAsync(string host, CancellationToken cancellationToken) => Task.FromResult(Serving);
    }

    private sealed record Setup(ApplicationDbContext Db, string RouteRoot, FakeResolver Resolver, Guid UserId)
    {
        public FakeProbe Probe { get; } = new();
    }

    private static async Task<Setup> CreateAsync(bool isPublic = true)
    {
        var db = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(AppId, runnerId);
        target.GenericRuntimeDeployed = true;
        AddMachineRunner(db, runnerId);
        AddApp(db, AppId, target, GenericRuntimeContractJson());
        var app = db.WebApps.Local.Single();
        app.RequiresAuthentication = false;
        app.IsPublished = isPublic;
        app.PublicUrl = "blog";
        app.RuntimeVisibility = AppRuntimeVisibilities.Public;
        app.AccessStatus = AppAccessStatuses.Active;
        db.WebAppUsers.Add(new WebAppUser { WebAppId = AppId, UserId = OwnerId });
        await db.SaveChangesAsync();
        return new Setup(db, CreateTempDirectory(), new FakeResolver(), OwnerId);
    }

    private static CustomDomainService Service(Setup setup, Guid? userId = null, List<string>? allowed = null, IAppRuntimeRestarter? restarter = null)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, (userId ?? setup.UserId).ToString())], "Test"))
        };
        var user = new UserContextService(new HttpContextAccessor { HttpContext = http });
        return new CustomDomainService(
            setup.Db,
            new ResourceAuthorizationService(setup.Db, user),
            Options.Create(new PublishedAppsConfig { AllowedCustomDomains = allowed ?? [] }),
            setup.Resolver,
            setup.Probe,
            CreateService(setup.Db, setup.RouteRoot, "letsencrypt"),
            restarter);
    }

    private static async Task ProveAsync(Setup setup, string host)
    {
        var domain = await setup.Db.AppCustomDomains.SingleAsync(d => d.Host == host);
        setup.Resolver.Txt["_automaton-challenge." + host] = ["automaton-verify=" + domain.VerificationToken];
        setup.Resolver.Records[host] = [ServerIp];
    }

    private static string Routes(Setup setup) => File.ReadAllText(Path.Combine(setup.RouteRoot, "managed-runtime-routes.yml"));

    [Test]
    public async Task AddedDomain_DoesNotRoute_UntilDnsPointsAtTheServer()
    {
        var setup = await CreateAsync();
        var state = await Service(setup).AddAsync(AppId, " Blog.Example.test. ", default);
        Assert.That(state.Domains.Single().Host, Is.EqualTo("blog.example.test"));
        Assert.That(state.Domains.Single().Verified, Is.False);
        Assert.That(state.TargetAddresses, Is.EqualTo(new[] { "203.0.113.10" }));

        var domainId = state.Domains.Single().Id;
        setup.Resolver.Txt[state.Domains.Single().ChallengeHost] = [state.Domains.Single().ChallengeValue];
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Service(setup).VerifyAsync(AppId, domainId, default));
        Assert.That(ex!.Message, Does.Contain("does not point at this server"));
        Assert.That(File.Exists(Path.Combine(setup.RouteRoot, "managed-runtime-routes.yml")), Is.False);
    }

    [Test]
    public async Task DnsPointingHere_IsNotEnough_WithoutTheOwnersTxtRecord()
    {
        var setup = await CreateAsync();
        var domainId = (await Service(setup).AddAsync(AppId, "blog.example.test", default)).Domains.Single().Id;
        setup.Resolver.Records["blog.example.test"] = [ServerIp];

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Service(setup).VerifyAsync(AppId, domainId, default));
        Assert.That(ex!.Message, Does.Contain("_automaton-challenge.blog.example.test"));

        // Someone else's token (a squatter's) does not prove this claim either.
        setup.Resolver.Txt["_automaton-challenge.blog.example.test"] = ["automaton-verify=0000"];
        Assert.ThrowsAsync<InvalidOperationException>(() => Service(setup).VerifyAsync(AppId, domainId, default));
        Assert.That(File.Exists(Path.Combine(setup.RouteRoot, "managed-runtime-routes.yml")), Is.False);
    }

    [Test]
    public async Task VerifiedDomain_NotServing_DoesNotBecomePrimaryOrRedirect_UntilItIsLive()
    {
        var setup = await CreateAsync();
        setup.Probe.Serving = false;
        var domainId = (await Service(setup).AddAsync(AppId, "blog.example.test", default)).Domains.Single().Id;
        await ProveAsync(setup, "blog.example.test");

        var state = await Service(setup).VerifyAsync(AppId, domainId, default);
        Assert.That(state.Domains.Single().Status, Is.EqualTo("Verified"));
        Assert.That(state.Domains.Single().IsPrimary, Is.False);
        Assert.That(Routes(setup), Does.Contain("blog.example.test"));
        Assert.That(Routes(setup), Does.Not.Contain("redirectregex").And.Not.Contain("redirect"));

        setup.Probe.Serving = true;
        await Service(setup).CheckAllLiveAsync(default);
        var live = (await Service(setup).GetAsync(AppId, default)).Domains.Single();
        Assert.That(live.Status, Is.EqualTo("Live"));
        Assert.That(live.IsPrimary, Is.True);
        Assert.That(Routes(setup), Does.Contain("redirect"));

        setup.Probe.Serving = false;
        await Service(setup).CheckAllLiveAsync(default);
        var dropped = (await Service(setup).GetAsync(AppId, default)).Domains.Single();
        Assert.That(dropped.IsPrimary, Is.False);
        Assert.That(Routes(setup), Does.Not.Contain("redirect"));
    }

    private sealed class CountingRestarter : IAppRuntimeRestarter
    {
        public int Restarts;
        public Task<int> RestartRunningAsync(Guid appId, CancellationToken token, bool wait = false) { Restarts++; return Task.FromResult(1); }
    }

    [Test]
    public async Task NewPrimaryDomain_RestartsAnAppWhoseSettingDefaultsToItsAddress_AndOnlyThen()
    {
        var setup = await CreateAsync();
        var restarter = new CountingRestarter();
        var domainId = (await Service(setup, restarter: restarter).AddAsync(AppId, "blog.example.test", default)).Domains.Single().Id;
        await ProveAsync(setup, "blog.example.test");

        await Service(setup, restarter: restarter).VerifyAsync(AppId, domainId, default);
        Assert.That(restarter.Restarts, Is.EqualTo(0));

        var app = await setup.Db.WebApps.SingleAsync(a => a.Id == AppId);
        app.ApplicationContractJson = """{"settings":[{"name":"URL","default":"{{app.address}}"}]}""";
        await setup.Db.SaveChangesAsync();
        await Service(setup, restarter: restarter).SetPrimaryAsync(AppId, domainId, default);
        Assert.That(restarter.Restarts, Is.EqualTo(1));
    }

    [Test]
    public async Task VerifiedDomain_RoutesWithOwnCertificate_AndTheOldAddressRedirects()
    {
        var setup = await CreateAsync();
        var domainId = (await Service(setup).AddAsync(AppId, "blog.example.test", default)).Domains.Single().Id;
        await ProveAsync(setup, "blog.example.test");

        var state = await Service(setup).VerifyAsync(AppId, domainId, default);

        Assert.That(state.Domains.Single(), Has.Property("IsPrimary").True.And.Property("Verified").True);
        var routes = Routes(setup);
        var id = AppId.ToString("N");
        Assert.Multiple(() =>
        {
            Assert.That(routes, Does.Contain("Host(`blog.example.test`) && PathPrefix(`/`)"));
            Assert.That(routes, Does.Contain($"domain-{domainId:N}-runtime"));
            Assert.That(routes, Does.Contain($"slug-{id}-redirect"));
            Assert.That(routes, Does.Contain("replacement: \"https://blog.example.test/${1}\""));
            Assert.That(routes, Does.Not.Contain($"slug-{id}-runtime"));
        });
    }

    [Test]
    public async Task RemovingTheDomain_StopsItsRouteAndRestoresTheOldAddress()
    {
        var setup = await CreateAsync();
        var domainId = (await Service(setup).AddAsync(AppId, "blog.example.test", default)).Domains.Single().Id;
        await ProveAsync(setup, "blog.example.test");
        await Service(setup).VerifyAsync(AppId, domainId, default);

        var state = await Service(setup).RemoveAsync(AppId, domainId, default);

        Assert.That(state.Domains, Is.Empty);
        var routes = Routes(setup);
        Assert.That(routes, Does.Not.Contain("blog.example.test"));
        Assert.That(routes, Does.Contain($"slug-{AppId:N}-runtime"));
    }

    [Test]
    public async Task SecondDomain_RedirectsToThePrimary_UntilMadePrimary()
    {
        var setup = await CreateAsync();
        var first = (await Service(setup).AddAsync(AppId, "blog.example.test", default)).Domains.Single().Id;
        var second = (await Service(setup).AddAsync(AppId, "www.example.test", default)).Domains.Last().Id;
        await ProveAsync(setup, "blog.example.test");
        await ProveAsync(setup, "www.example.test");
        await Service(setup).VerifyAsync(AppId, first, default);
        await Service(setup).VerifyAsync(AppId, second, default);
        Assert.That(Routes(setup), Does.Contain($"domain-{second:N}-redirect"));

        await Service(setup).SetPrimaryAsync(AppId, second, default);

        var routes = Routes(setup);
        Assert.That(routes, Does.Contain($"domain-{first:N}-redirect"));
        Assert.That(routes, Does.Contain($"domain-{second:N}-runtime"));
        Assert.That(routes, Does.Contain("https://www.example.test/${1}"));
    }

    [Test]
    public async Task AppThatIsNoLongerPublic_StopsRoutingItsDomain()
    {
        var setup = await CreateAsync();
        var domainId = (await Service(setup).AddAsync(AppId, "blog.example.test", default)).Domains.Single().Id;
        await ProveAsync(setup, "blog.example.test");
        await Service(setup).VerifyAsync(AppId, domainId, default);
        setup.Db.WebApps.Single().IsPublished = false;
        await setup.Db.SaveChangesAsync();

        await CreateService(setup.Db, setup.RouteRoot, "letsencrypt").RefreshAsync();

        Assert.That(File.ReadAllText(Path.Combine(setup.RouteRoot, "managed-runtime-routes.yml")), Does.Not.Contain("blog.example.test"));
    }

    [TestCase("not a domain")]
    [TestCase("localhost")]
    [TestCase("blog.localhost")]
    [TestCase("203.0.113.5")]
    [TestCase("")]
    public async Task Add_RejectsInvalidDomains(string host)
    {
        var setup = await CreateAsync();
        Assert.ThrowsAsync<InvalidOperationException>(() => Service(setup).AddAsync(AppId, host, default));
    }

    [Test]
    public async Task Add_RejectsDomainsOutsideTheAdministratorsList_AndDuplicates()
    {
        var setup = await CreateAsync();
        var service = Service(setup, allowed: ["example.test"]);
        Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(AppId, "blog.other.test", default));
        await service.AddAsync(AppId, "blog.example.test", default);
        Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(AppId, "BLOG.example.test", default));
    }

    [Test]
    public async Task Add_RequiresAPublicApp()
    {
        var setup = await CreateAsync(isPublic: false);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Service(setup).AddAsync(AppId, "blog.example.test", default));
        Assert.That(ex!.Message, Does.Contain("open to anyone on the internet"));
    }

    [Test]
    public async Task OnlyPeopleWhoManageTheApp_CanChangeItsDomains()
    {
        var setup = await CreateAsync();
        var stranger = Service(setup, Guid.NewGuid());
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => stranger.AddAsync(AppId, "blog.example.test", default));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => stranger.GetAsync(AppId, default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => Service(setup).GetAsync(Guid.NewGuid(), default));
    }
}
