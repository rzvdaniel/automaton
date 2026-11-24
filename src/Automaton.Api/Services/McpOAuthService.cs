using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.Services;

/// <summary>
/// OAuth 2.1 for AI connectors (Claude custom connectors) to the /mcp endpoint: dynamic client registration, authorization code
/// with PKCE (S256 only) and refresh. Clients, codes and refresh tokens are signed and encrypted with Data Protection, so
/// nothing is stored except the access token, which is a Developer API key (listed and revoked under Settings, API Keys).
/// </summary>
public sealed class McpOAuthService
{
    public const int AccessTokenSeconds = 3600;
    internal const int RefreshTokenDays = 30;
    private const int CodeMinutes = 5;
    private const int MaxRedirectUris = 10;
    private const int MaxUriLength = 500;
    private const int MaxVerifierLength = 128;

    private static readonly object CodeLock = new();

    private readonly ApiKeyService apiKeyService;
    private readonly IMemoryCache cache;
    private readonly IDataProtector clientProtector;
    private readonly IDataProtector codeProtector;
    private readonly IDataProtector refreshProtector;

    public McpOAuthService(ApiKeyService apiKeyService, IMemoryCache cache, IDataProtectionProvider provider)
    {
        this.apiKeyService = apiKeyService;
        this.cache = cache;
        clientProtector = provider.CreateProtector("Automaton.McpOAuth.Client.v1");
        codeProtector = provider.CreateProtector("Automaton.McpOAuth.Code.v1");
        refreshProtector = provider.CreateProtector("Automaton.McpOAuth.Refresh.v1");
    }

    public sealed record ClientInfo(string Name, string[] RedirectUris);

    public sealed record TokenResult(string AccessToken, string RefreshToken, int ExpiresIn);

    private sealed record CodePayload(string U, string C, string R, string H, DateTime E, string N);

    private sealed record RefreshPayload(Guid K, string F, string C, DateTime E);

    /// <summary>Registers a client. Returns the client_id, or null when a redirect URI is not acceptable.</summary>
    public (string ClientId, ClientInfo Info)? Register(string? name, IReadOnlyList<string>? redirectUris)
    {
        if (redirectUris is not { Count: > 0 and <= MaxRedirectUris } || !redirectUris.All(IsAcceptableRedirectUri))
            return null;

        var clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) clean = "AI connector";
        if (clean.Length > 60) clean = clean[..60];
        var info = new ClientInfo(clean, redirectUris.Distinct().ToArray());
        return (clientProtector.Protect(JsonSerializer.Serialize(info)), info);
    }

    public ClientInfo? ReadClient(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId) || clientId.Length > 4000) return null;
        try { return JsonSerializer.Deserialize<ClientInfo>(clientProtector.Unprotect(clientId)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return null; }
    }

    /// <summary>Redirect URIs must be https, or http on a loopback address, with no fragment (RFC 8252, OAuth 2.1).</summary>
    internal static bool IsAcceptableRedirectUri(string uri)
    {
        if (uri.Length > MaxUriLength || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Fragment.Length > 0
            || parsed.UserInfo.Length > 0)
            return false;
        return parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback);
    }

    /// <summary>The request is valid when the client is registered, the redirect URI is one it registered, and PKCE S256 is used.</summary>
    public ClientInfo? ValidateAuthorization(string? clientId, string? redirectUri, string? codeChallenge, string? method)
    {
        var client = ReadClient(clientId);
        if (client is null || redirectUri is null || !client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal)) return null;
        if (method != "S256" || codeChallenge is not { Length: >= 43 and <= 128 }) return null;
        return client;
    }

    public string IssueCode(Guid userId, string clientId, string redirectUri, string codeChallenge) =>
        codeProtector.Protect(JsonSerializer.Serialize(new CodePayload(
            userId.ToString(), clientId, redirectUri, codeChallenge, DateTime.UtcNow.AddMinutes(CodeMinutes), Guid.NewGuid().ToString("N"))));

    /// <summary>Exchanges a code (once only) for tokens. Null for every kind of failure, so nothing is revealed.</summary>
    public async Task<TokenResult?> ExchangeCodeAsync(string? code, string? verifier, string? clientId, string? redirectUri)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 4000 || string.IsNullOrEmpty(verifier) || verifier.Length is < 43 or > MaxVerifierLength) return null;
        CodePayload? payload;
        try { payload = JsonSerializer.Deserialize<CodePayload>(codeProtector.Unprotect(code)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return null; }
        if (payload is null || payload.E <= DateTime.UtcNow || payload.C != clientId || payload.R != redirectUri) return null;

        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(payload.H))) return null;

        // Single use: the first exchange wins, a replay within the code's lifetime is refused.
        lock (CodeLock)
        {
            if (cache.TryGetValue("mcp-oauth-code:" + payload.N, out _)) return null;
            cache.Set("mcp-oauth-code:" + payload.N, true, TimeSpan.FromMinutes(CodeMinutes + 1));
        }

        var client = ReadClient(clientId);
        if (client is null || !Guid.TryParse(payload.U, out var userId)) return null;
        var (rawKey, entity) = await apiKeyService.CreateApiKeyAsync(
            userId,
            $"Claude connector - {client.Name}",
            ApiKeyScope.Developer,
            "Signed in through the MCP connector sign-in. Revoke it here to cut the connection.",
            DateTime.UtcNow.AddSeconds(AccessTokenSeconds),
            source: ApiKeySource.McpConnector);
        return new TokenResult(rawKey, ProtectRefresh(entity.Id, entity.HashedKey, clientId!), AccessTokenSeconds);
    }

    /// <summary>Rotates the connection's key. A used or revoked refresh token fails.</summary>
    public async Task<TokenResult?> RefreshAsync(string? refreshToken, string? clientId)
    {
        if (string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 4000) return null;
        RefreshPayload? payload;
        try { payload = JsonSerializer.Deserialize<RefreshPayload>(refreshProtector.Unprotect(refreshToken)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return null; }
        if (payload is null || payload.E <= DateTime.UtcNow || (clientId is not null && payload.C != clientId)) return null;

        var rotated = await apiKeyService.RotateApiKeyAsync(payload.K, payload.F, DateTime.UtcNow.AddSeconds(AccessTokenSeconds));
        return rotated is null
            ? null
            : new TokenResult(rotated.Value.ApiKey, ProtectRefresh(payload.K, rotated.Value.Entity.HashedKey, payload.C), AccessTokenSeconds);
    }

    private string ProtectRefresh(Guid keyId, string hashedKey, string clientId) =>
        refreshProtector.Protect(JsonSerializer.Serialize(new RefreshPayload(
            keyId, ApiKeyService.HashedKeyFingerprint(hashedKey), clientId, DateTime.UtcNow.AddDays(RefreshTokenDays))));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
