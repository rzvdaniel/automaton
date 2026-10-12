using Automaton.Api.Data;
using Automaton.Api.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Automaton.Api.Services;

public sealed class NodeEnrollmentService
{
    private readonly NodeEnrollmentBootstrapHandler bootstrap;
    private readonly NodeEnrollmentRecoveryHandler recovery;
    private readonly NodeManagementAuthenticationHandler authentication;

    public NodeEnrollmentService(
        ApplicationDbContext db,
        IDataProtectionProvider protectionProvider,
        TimeProvider timeProvider,
        NodeProxyCertificateValidator proxyCertificateValidator)
    {
        bootstrap = new NodeEnrollmentBootstrapHandler(db, protectionProvider, timeProvider);
        recovery = new NodeEnrollmentRecoveryHandler(db, protectionProvider, timeProvider);
        authentication = new NodeManagementAuthenticationHandler(db, timeProvider, proxyCertificateValidator);
    }

    public Task<NodeEnrollmentOperationResult> BootstrapAsync(Guid enrollmentId, NodeBootstrapRequest request, CancellationToken cancellationToken) =>
        bootstrap.BootstrapAsync(enrollmentId, request, cancellationToken);

    public Task<NodeEnrollmentChallengeResult> CreateRecoveryChallengeAsync(Guid enrollmentId, Guid installationId, Guid realmId, CancellationToken cancellationToken) =>
        recovery.CreateChallengeAsync(enrollmentId, installationId, realmId, cancellationToken);

    public Task<NodeEnrollmentOperationResult> CompleteRecoveryAsync(Guid enrollmentId, NodeRecoveryCompletionRequest request, CancellationToken cancellationToken) =>
        recovery.CompleteAsync(enrollmentId, request, cancellationToken);

    public Task<NodeIdentityResult> AuthenticateManagementRequestAsync(
        Microsoft.AspNetCore.Http.HttpContext context,
        string? apiKey,
        CancellationToken cancellationToken) =>
        authentication.AuthenticateAsync(context, apiKey, cancellationToken);
}

public sealed record NodeBootstrapRequest(string EnrollmentToken, string CsrDerBase64, string CertificateDerBase64);
public sealed record NodeRecoveryCompletionRequest(Guid ChallengeId, Guid InstallationId, Guid ControlPlaneRealmId, string SignatureBase64);
public sealed record NodeEnrollmentReceipt(Guid EnrollmentId, Guid NodeId, Guid InstallationId, Guid ControlPlaneRealmId, string CertificateSha256, DateTime CommittedAtUtc);
public sealed record NodeEnrollmentCredentialBundle(NodeEnrollmentReceipt Receipt, string NodeApiKey);
public sealed record NodeEnrollmentOperationResult(bool Accepted, bool InvalidInput, NodeEnrollmentReceipt? Receipt, NodeEnrollmentCredentialBundle? Credentials)
{
    public static NodeEnrollmentOperationResult Invalid => new(false, true, null, null);
    public static NodeEnrollmentOperationResult Rejected => new(false, false, null, null);
    public static NodeEnrollmentOperationResult Success(NodeEnrollmentReceipt receipt, NodeEnrollmentCredentialBundle? credentials) => new(true, false, receipt, credentials);
}
public sealed record NodeEnrollmentChallengeResult(bool Accepted, Guid ChallengeId, string? NonceBase64Url, DateTime? ExpiresAtUtc)
{
    public static NodeEnrollmentChallengeResult Rejected => new(false, Guid.Empty, null, null);
    public static NodeEnrollmentChallengeResult Success(Guid id, byte[] nonce, DateTime expiresAtUtc) => new(
        true, id, Convert.ToBase64String(nonce).TrimEnd('=').Replace('+', '-').Replace('/', '_'), expiresAtUtc);
}
public sealed record NodeIdentityResult(bool Accepted, Guid NodeId, Guid InstallationId, Guid ControlPlaneRealmId)
{
    public static NodeIdentityResult Rejected => new(false, Guid.Empty, Guid.Empty, Guid.Empty);
    public static NodeIdentityResult Success(Guid nodeId, Guid installationId, Guid realmId) => new(true, nodeId, installationId, realmId);
}
