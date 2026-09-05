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

        public static async Task<AuthenticationSetup> CreateAsync(string role)
        {
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();

            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<ApplicationDbContext>();
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();

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

            var apiKeyService = new ApiKeyService(
                db,
                NullLogger<ApiKeyService>.Instance,
                authorizationService: null!,
                new NoOpAuditEventService());
            var created = await apiKeyService.CreateApiKeyAsync(user.Id, "test", ApiKeyScope.Developer);

            return new AuthenticationSetup(provider, db, apiKeyService, created.apiKey);
        }

        public async Task<AuthenticateResult> AuthenticateAsync()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-API-Key"] = apiKey;
            var handler = new ApiKeyAuthenticationHandler(
                new SingleOptionsMonitor<ApiKeyAuthenticationSchemeOptions>(new()),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                apiKeyService,
                db,
                services.GetRequiredService<UserManager<ApplicationUser>>());

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
