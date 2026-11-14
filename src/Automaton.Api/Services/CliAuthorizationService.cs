using System.Security.Cryptography;
using System.Text;
using System.Data;
using Microsoft.AspNetCore.DataProtection;
using Automaton.Api.Authentication;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public class CliAuthorizationService
{
    // 5 s keeps polling (plus create and complete) under the 20 requests a minute per IP limit.
    private const int PollIntervalSeconds = 5;

    private const int LifetimeMinutes = 10;
    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly ApplicationDbContext dbContext;
    private readonly ApiKeyService apiKeyService;
    private readonly IAuditEventService auditEventService;
    private readonly ILogger<CliAuthorizationService> logger;
    private readonly IDataProtector credentialProtector;
    private readonly UserManager<ApplicationUser> userManager;

    public CliAuthorizationService(
        ApplicationDbContext dbContext,
        ApiKeyService apiKeyService,
        IAuditEventService auditEventService,
        ILogger<CliAuthorizationService> logger,
        IDataProtectionProvider dataProtectionProvider,
        UserManager<ApplicationUser> userManager)
    {
        this.userManager = userManager;
        this.dbContext = dbContext;
        this.apiKeyService = apiKeyService;
        this.auditEventService = auditEventService;
        this.logger = logger;
        credentialProtector = dataProtectionProvider.CreateProtector("Automaton.CliAuthorizationCredential.v1");
    }

    public async Task<CliAuthorizationCreated> CreateAsync(
        string? deviceName,
        string? remoteIp,
        CancellationToken cancellationToken,
        ApiKeyScope scope = ApiKeyScope.Developer)
    {
        if (scope is not (ApiKeyScope.Developer or ApiKeyScope.PermissionsAdmin))
            throw new ArgumentOutOfRangeException(nameof(scope), "Only the Developer and PermissionsAdmin key types can be requested.");
        var verifier = CreateToken(32);
        var userCode = await CreateUniqueUserCodeAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var request = new CliAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            UserCode = userCode,
            HashedVerifier = Hash(verifier),
            DeviceName = NormalizeDeviceName(deviceName),
            RemoteIp = remoteIp,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(scope == ApiKeyScope.PermissionsAdmin ? PermissionsAdminKey.LoginRequestLifetimeMinutes : LifetimeMinutes),
            RequestedScope = scope
        };

        dbContext.CliAuthorizationRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CliAuthorizationCreated(
            request.Id,
            request.UserCode,
            verifier,
            request.ExpiresAt,
            PollIntervalSeconds);
    }

    public async Task<bool> ApproveAsync(
        Guid requestId,
        string userCode,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.CliAuthorizationRequests
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null || request.ExpiresAt <= DateTime.UtcNow || request.CompletedAt.HasValue
            || !string.Equals(request.UserCode, userCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.ApprovedAt.HasValue && request.UserId != userId)
        {
            return false;
        }

        request.UserId = userId;
        request.ApprovedAt ??= DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CliAuthorizationPollResult> PollAsync(
        Guid requestId,
        string verifier,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var request = await dbContext.CliAuthorizationRequests
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null || !CryptographicEquals(request.HashedVerifier, Hash(verifier)))
        {
            return CliAuthorizationPollResult.Invalid;
        }

        if (request.ExpiresAt <= DateTime.UtcNow)
        {
            return CliAuthorizationPollResult.Expired;
        }

        if (request.CancelledAt.HasValue)
        {
            return CliAuthorizationPollResult.Cancelled;
        }

        if (!request.ApprovedAt.HasValue || !request.UserId.HasValue)
        {
            return CliAuthorizationPollResult.Pending;
        }

        if (!string.IsNullOrWhiteSpace(request.EncryptedApiKey))
        {
            var existingKey = credentialProtector.Unprotect(request.EncryptedApiKey);
            return new CliAuthorizationPollResult(existingKey, request.UserId.Value);
        }

        if (request.CompletedAt.HasValue)
        {
            return CliAuthorizationPollResult.Completed;
        }

        if (request.RequestedScope == ApiKeyScope.PermissionsAdmin && !await IsActiveAdministratorAsync(request.UserId.Value))
        {
            // The approver lost the role (or was locked out) between approval and the CLI's poll: no key.
            request.CancelledAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return CliAuthorizationPollResult.Denied;
        }

        var rawKey = await IssueCredentialAsync(request, request.UserId.Value, cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("CLI authorization completed for user {UserId} on device {DeviceName}", request.UserId, request.DeviceName);
        return new CliAuthorizationPollResult(rawKey, request.UserId.Value);
    }

    // The key is created only when the CLI polls after explicit browser approval.
    // It remains encrypted until the CLI acknowledges that it saved the credential,
    // allowing safe retries if the first response is interrupted.
    private async Task<string> IssueCredentialAsync(CliAuthorizationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var permissionsAdmin = request.RequestedScope == ApiKeyScope.PermissionsAdmin;
        var (rawKey, entity) = await apiKeyService.CreateApiKeyAsync(
            userId,
            permissionsAdmin ? $"CLI permissions admin - {request.DeviceName}" : $"CLI - {request.DeviceName}",
            permissionsAdmin ? ApiKeyScope.PermissionsAdmin : ApiKeyScope.Developer,
            permissionsAdmin
                ? "Admin key to approve app permissions, created by Automaton CLI authorization."
                : "Developer credential created by Automaton CLI authorization.",
            expiresAt: permissionsAdmin ? DateTime.UtcNow.AddDays(PermissionsAdminKey.DefaultLifetimeDays) : null,
            saveChanges: false,
            source: ApiKeySource.CliAuthorization);

        request.EncryptedApiKey = credentialProtector.Protect(rawKey);
        request.CredentialIssuedAt = DateTime.UtcNow;
        request.ApiKeyId = entity.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteCredentialIssuedAuditAsync(request, entity, cancellationToken);
        return rawKey;
    }

    private Task WriteCredentialIssuedAuditAsync(
        CliAuthorizationRequest request,
        ApiKey entity,
        CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.ApiKey,
            EntityId = entity.Id,
            EventType = AuditEventTypes.ApiKeyCreated,
            Message = $"API key '{entity.Name}' was created",
            Outcome = AuditOutcomes.Success,
            FailOnError = true,
            ActorUserId = request.UserId,
            Metadata = new
            {
                entity.Name,
                entity.Scope,
                entity.ExpiresAt,
                entity.KeyPrefix,
                Source = "cli-device-authorization",
                RequestId = request.Id,
                request.DeviceName,
                request.RemoteIp,
                ApproverUserId = request.UserId
            }
        }, cancellationToken);

    /// <summary>True only for a user who is a system administrator and not locked out, read from the database.</summary>
    public virtual async Task<bool> IsActiveAdministratorAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is not null
            && !await userManager.IsLockedOutAsync(user)
            && PermissionsAdminKey.IsAdministrator(await userManager.GetRolesAsync(user));
    }

    /// <summary>
    /// Whether this user may approve the request. An admin key request needs a system administrator signed in to Studio
    /// (never an API key), re-read from the database; a refusal is audited and nothing is created.
    /// </summary>
    public async Task<bool> MayApproveAsync(Guid requestId, Guid userId, bool viaApiKey, CancellationToken cancellationToken)
    {
        var request = await dbContext.CliAuthorizationRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (request is null || request.RequestedScope != ApiKeyScope.PermissionsAdmin)
            return true;
        if (!viaApiKey && await IsActiveAdministratorAsync(userId))
            return true;

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.ApiKey,
            EntityId = request.Id,
            EventType = AuditEventTypes.ApiKeyCreated,
            Message = "Approval of an admin CLI key was refused",
            Outcome = AuditOutcomes.Denied,
            ActorUserId = userId,
            Metadata = new { RequestId = request.Id, request.DeviceName, request.RemoteIp, Scope = request.RequestedScope, ViaApiKey = viaApiKey }
        }, cancellationToken);
        return false;
    }

    public async Task<ApiKeyScope?> GetRequestedScopeAsync(Guid requestId, CancellationToken cancellationToken)
        => await dbContext.CliAuthorizationRequests.AsNoTracking()
            .Where(x => x.Id == requestId)
            .Select(x => (ApiKeyScope?)x.RequestedScope)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> CompleteAsync(
        Guid requestId,
        string verifier,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var request = await dbContext.CliAuthorizationRequests
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null || !CryptographicEquals(request.HashedVerifier, Hash(verifier)))
            return false;

        if (request.CompletedAt.HasValue && string.IsNullOrWhiteSpace(request.EncryptedApiKey))
            return true;

        if (string.IsNullOrWhiteSpace(request.EncryptedApiKey) || !request.ApprovedAt.HasValue)
            return false;

        request.EncryptedApiKey = null;
        request.CompletedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<CliAuthorizationDetails?> GetDetailsAsync(
        Guid requestId,
        string userCode,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.CliAuthorizationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null || !string.Equals(request.UserCode, userCode.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        var now = DateTime.UtcNow;
        var status = "pending";
        if (request.ApprovedAt.HasValue)
            status = "approved";
        if (request.CompletedAt.HasValue)
            status = "completed";
        if (request.CancelledAt.HasValue)
            status = "cancelled";
        if (request.ExpiresAt <= now && !request.CompletedAt.HasValue)
            status = "expired";

        return new CliAuthorizationDetails(
            request.DeviceName,
            request.RemoteIp,
            request.CreatedAt,
            request.ExpiresAt,
            status,
            request.RequestedScope.ToString());
    }

    public async Task<bool> CancelAsync(
        Guid requestId,
        string userCode,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.CliAuthorizationRequests
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null || !string.Equals(request.UserCode, userCode.Trim(), StringComparison.OrdinalIgnoreCase)
            || request.CompletedAt.HasValue || request.CancelledAt.HasValue
            || !string.IsNullOrWhiteSpace(request.EncryptedApiKey) || request.ExpiresAt <= DateTime.UtcNow)
        {
            return false;
        }

        request.CancelledAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string> CreateUniqueUserCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = $"{CreateCodePart()}-{CreateCodePart()}";
            if (!await dbContext.CliAuthorizationRequests.AnyAsync(x => x.UserCode == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a CLI authorization code.");
    }

    private static string CreateCodePart()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(4, bytes.ToArray(), (buffer, values) =>
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = UserCodeAlphabet[values[i] % UserCodeAlphabet.Length];
            }
        });
    }

    private static string CreateToken(int byteCount)
        => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool CryptographicEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private static string NormalizeDeviceName(string? deviceName)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceName) ? "This device" : deviceName.Trim();
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }
}

public sealed record CliAuthorizationCreated(
    Guid RequestId,
    string UserCode,
    string Verifier,
    DateTime ExpiresAt,
    int PollInterval);

public sealed record CliAuthorizationDetails(
    string DeviceName,
    string? RemoteIp,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string Status,
    string Scope);

public sealed class CliAuthorizationPollResult
{
    private CliAuthorizationPollResult(string? apiKey, Guid? userId, string status)
    {
        ApiKey = apiKey;
        UserId = userId;
        Status = status;
    }

    public string? ApiKey { get; }
    public Guid? UserId { get; }
    public string Status { get; }
    public bool IsInvalid => Status == "invalid";

    public static CliAuthorizationPollResult Pending => new(null, null, "pending");
    public static CliAuthorizationPollResult Expired => new(null, null, "expired");
    public static CliAuthorizationPollResult Completed => new(null, null, "completed");
    public static CliAuthorizationPollResult Cancelled => new(null, null, "cancelled");
    public static CliAuthorizationPollResult Denied => new(null, null, "denied");
    public static CliAuthorizationPollResult Invalid => new(null, null, "invalid");
    public CliAuthorizationPollResult(string apiKey, Guid userId) : this(apiKey, userId, "authorized") { }
}
