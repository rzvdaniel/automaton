using System.Text.Encodings.Web;
using Automaton.Api.Authentication;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ApiKeyAuthenticationHandlerTests
{
    [TestCase("Admin")]
    [TestCase("SysAdmin")]
    public async Task AuthenticateAsync_EmitsCurrentUserRoleClaims(string role)
    {
        await using var setup = await AuthenticationSetup.CreateAsync(role);

        var result = await setup.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal!.IsInRole(role), Is.True);
    }

    [Test]
    public async Task AuthenticateAsync_DoesNotGrantAdministratorRoleToOrdinaryUsers()
    {
        await using var setup = await AuthenticationSetup.CreateAsync("User");

        var result = await setup.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal!.IsInRole("Admin"), Is.False);
        Assert.That(result.Principal.IsInRole("SysAdmin"), Is.False);
    }

    private static readonly string AppId = Guid.NewGuid().ToString();

    [TestCase("/api/admin/apps/{0}/permissions")]
    [TestCase("/api/admin/apps/{0}/permissions/runner:maintain")]
    [TestCase("/api/admin/apps/{0}/runner-permissions")]
    [TestCase("/api/admin/app-permissions/pending")]
    public async Task PermissionsAdminKey_AuthenticatesOnTheApprovalPathsWithOnlyTheSysAdminRole(string path)
    {
        await using var setup = await AuthenticationSetup.CreateAsync("SysAdmin", ApiKeyScope.PermissionsAdmin);

        var result = await setup.AuthenticateAsync(string.Format(path, AppId));

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal!.IsInRole("SysAdmin"), Is.True);
        Assert.That(result.Principal.FindFirst("api_key_scope")!.Value, Is.EqualTo("PermissionsAdmin"));
    }

    [TestCase("/api/apps")]
    [TestCase("/api/admin/users")]
    [TestCase("/api/admin/backups")]
    [TestCase("/api/admin/apps/{0}")]
    [TestCase("/api/admin/apps/{0}/permissions/../files")]
    [TestCase("/api/admin/apps/{0}/permissions/a/b")]
    [TestCase("/api/api-keys")]
    public async Task PermissionsAdminKey_IsRefusedEverywhereElse(string path)
    {
        await using var setup = await AuthenticationSetup.CreateAsync("SysAdmin", ApiKeyScope.PermissionsAdmin);

        var result = await setup.AuthenticateAsync(string.Format(path, AppId));

        Assert.That(result.Succeeded, Is.False);
    }

    [TestCase("User")]
    [TestCase("Admin")]
    public async Task PermissionsAdminKey_StopsWorkingWhenItsOwnerIsNoLongerASystemAdministrator(string role)
    {
        await using var setup = await AuthenticationSetup.CreateAsync(role, ApiKeyScope.PermissionsAdmin);

        var result = await setup.AuthenticateAsync($"/api/admin/apps/{AppId}/permissions");

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task PermissionsAdminKey_ExpiredKeyIsRefused()
    {
        await using var setup = await AuthenticationSetup.CreateAsync("SysAdmin", ApiKeyScope.PermissionsAdmin, DateTime.UtcNow.AddMinutes(-1));

        var result = await setup.AuthenticateAsync($"/api/admin/apps/{AppId}/permissions");

        Assert.That(result.Succeeded, Is.False);
    }

    private sealed class AuthenticationSetup : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly ApplicationDbContext db;
        private readonly ApiKeyService apiKeyService;
        private readonly string apiKey;

        private AuthenticationSetup(ServiceProvider services, ApplicationDbContext db, ApiKeyService apiKeyService, string apiKey)
        {
            this.services = services;
            this.db = db;
            this.apiKeyService = apiKeyService;
            this.apiKey = apiKey;
        }

        public static async Task<AuthenticationSetup> CreateAsync(string role, ApiKeyScope scope = ApiKeyScope.Developer, DateTime? expiresAt = null)
        {
            var provider = CreateServiceProvider();
            var db = provider.GetRequiredService<ApplicationDbContext>();
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();
            var user = await CreateUserAsync(userManager, roleManager, role);
            var (apiKeyService, apiKey) = await CreateApiKeyAsync(db, user.Id, scope, expiresAt);
            return new AuthenticationSetup(provider, db, apiKeyService, apiKey);
        }

        private static ServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            return services.BuildServiceProvider();
        }

        private static async Task<ApplicationUser> CreateUserAsync(
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            string role)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "api-key-user",
                Email = "api-key-user@example.test",
                PublicUsername = "api-key-user"
            };
            Assert.That((await userManager.CreateAsync(user)).Succeeded, Is.True);
            Assert.That((await roleManager.CreateAsync(new ApplicationRole { Name = role })).Succeeded, Is.True);
            Assert.That((await userManager.AddToRoleAsync(user, role)).Succeeded, Is.True);
            return user;
        }

        private static async Task<(ApiKeyService Service, string ApiKey)> CreateApiKeyAsync(
            ApplicationDbContext db,
            Guid userId,
            ApiKeyScope scope,
            DateTime? expiresAt)
        {
            var apiKeyService = new ApiKeyService(
                db,
                NullLogger<ApiKeyService>.Instance,
                authorizationService: null!,
                new NoOpAuditEventService());
            var created = await apiKeyService.CreateApiKeyAsync(userId, "test", scope, expiresAt: expiresAt);
            return (apiKeyService, created.apiKey);
        }

        public async Task<AuthenticateResult> AuthenticateAsync(string path = "/api/apps")
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Request.Headers["X-API-Key"] = apiKey;
            var handler = new ApiKeyAuthenticationHandler(
                new SingleOptionsMonitor<ApiKeyAuthenticationSchemeOptions>(new()),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                apiKeyService,
                db,
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

            await handler.InitializeAsync(
                new AuthenticationScheme(ApiKeyAuthenticationSchemeOptions.DefaultScheme, null, handler.GetType()),
                context);
            return await handler.AuthenticateAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            await services.DisposeAsync();
        }
    }

    private sealed class SingleOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable OnChange(Action<T, string?> listener) => EmptyDisposable.Instance;

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
