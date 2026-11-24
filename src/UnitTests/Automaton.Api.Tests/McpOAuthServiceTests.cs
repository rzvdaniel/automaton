using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class McpOAuthServiceTests
{
    private const string Redirect = "https://claude.ai/api/mcp/auth_callback";
    private ApplicationDbContext db = null!;
    private ApiKeyService keys = null!;
    private McpOAuthService oauth = null!;
    private Guid userId;

    [SetUp]
    public void SetUp()
    {
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        userId = Guid.NewGuid();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")) } };
        keys = new ApiKeyService(db, NullLogger<ApiKeyService>.Instance, null!, new AuditEventService(db, new UserContextService(accessor), accessor));
        oauth = new McpOAuthService(keys, new MemoryCache(new MemoryCacheOptions()), new EphemeralDataProtectionProvider());
    }

    [TearDown]
    public void TearDown() => db.Dispose();

    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    private string RegisterClient() => oauth.Register("Claude", [Redirect])!.Value.ClientId;

    [TestCase("http://evil.example/cb")]
    [TestCase("javascript:alert(1)")]
    [TestCase("https://claude.ai/cb#frag")]
    [TestCase("not a uri")]
    public void Register_RefusesUnsafeRedirectUris(string uri) => Assert.That(oauth.Register("x", [uri]), Is.Null);

    [Test]
    public void Register_AllowsLoopbackHttp() => Assert.That(oauth.Register("x", ["http://127.0.0.1:33418/cb"]), Is.Not.Null);

    [Test]
    public void ValidateAuthorization_RequiresRegisteredRedirectAndS256()
    {
        var clientId = RegisterClient();
        var (_, challenge) = Pkce();
        Assert.That(oauth.ValidateAuthorization(clientId, Redirect, challenge, "S256"), Is.Not.Null);
        Assert.That(oauth.ValidateAuthorization(clientId, "https://evil.example/cb", challenge, "S256"), Is.Null);
        Assert.That(oauth.ValidateAuthorization(clientId, Redirect, challenge, "plain"), Is.Null);
        Assert.That(oauth.ValidateAuthorization(clientId, Redirect, null, "S256"), Is.Null);
        Assert.That(oauth.ValidateAuthorization("forged", Redirect, challenge, "S256"), Is.Null);
    }

    [Test]
    public async Task CodeExchange_IssuesDeveloperKeyForTheApprovingUserOnly_AndOnlyOnce()
    {
        var clientId = RegisterClient();
        var (verifier, challenge) = Pkce();
        var code = oauth.IssueCode(userId, clientId, Redirect, challenge);

        Assert.That(await oauth.ExchangeCodeAsync(code, verifier, clientId, "https://other.example/cb"), Is.Null);
        Assert.That(await oauth.ExchangeCodeAsync(code, Pkce().Verifier, clientId, Redirect), Is.Null);

        var tokens = await oauth.ExchangeCodeAsync(code, verifier, clientId, Redirect);
        Assert.That(tokens, Is.Not.Null);
        var key = await keys.ValidateApiKeyAsync(tokens!.AccessToken);
        Assert.That(key!.Scope, Is.EqualTo(ApiKeyScope.Developer));
        Assert.That(key.Source, Is.EqualTo(ApiKeySource.McpConnector));
        Assert.That(key.ExpiresAt, Is.LessThan(DateTime.UtcNow.AddHours(2)));
        Assert.That((await db.ApiKeyUsers.SingleAsync(x => x.ApiKeyId == key.Id)).UserId, Is.EqualTo(userId));

        Assert.That(await oauth.ExchangeCodeAsync(code, verifier, clientId, Redirect), Is.Null, "a code works once");
    }

    [Test]
    public async Task Refresh_RotatesInPlace_RefusesReplayAndRevokedKeys()
    {
        var clientId = RegisterClient();
        var (verifier, challenge) = Pkce();
        var first = (await oauth.ExchangeCodeAsync(oauth.IssueCode(userId, clientId, Redirect, challenge), verifier, clientId, Redirect))!;

        var second = await oauth.RefreshAsync(first.RefreshToken, clientId);
        Assert.That(second, Is.Not.Null);
        Assert.That(await keys.ValidateApiKeyAsync(first.AccessToken), Is.Null, "the old access token stops working");
        Assert.That(await keys.ValidateApiKeyAsync(second!.AccessToken), Is.Not.Null);
        Assert.That(await db.ApiKeys.CountAsync(), Is.EqualTo(1), "one listed connection");
        Assert.That(await oauth.RefreshAsync(first.RefreshToken, clientId), Is.Null, "a used refresh token is refused");
        Assert.That(await oauth.RefreshAsync(second.RefreshToken, "other-client"), Is.Null);

        db.ApiKeys.Remove(await db.ApiKeys.SingleAsync());
        await db.SaveChangesAsync();
        Assert.That(await oauth.RefreshAsync(second.RefreshToken, clientId), Is.Null, "revoking in Studio cuts the connection");
    }

    [Test]
    public async Task Refresh_DoesNotRotateOtherKinds()
    {
        var (_, manual) = await keys.CreateApiKeyAsync(userId, "mine", ApiKeyScope.Developer);
        Assert.That(await keys.RotateApiKeyAsync(manual.Id, ApiKeyService.HashedKeyFingerprint(manual.HashedKey), DateTime.UtcNow.AddHours(1)), Is.Null);
    }
}
