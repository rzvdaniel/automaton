using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Automaton.Node.Preparation;

namespace Automaton.Node.Activation;

internal sealed class NodeActivationClient(HttpClient httpClient, Uri managementOrigin, INodeActivationIntentLedger intentLedger)
{
    private const int MaximumResponseBytes = 64 * 1024;
    private const int MaximumTransportAttempts = 4;
    private static readonly Regex LowerSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public NodeActivationClient(HttpClient httpClient, string? origin, INodeActivationIntentLedger intentLedger)
        : this(httpClient, ParseOrigin(origin), intentLedger) { }

    public async Task<NodeActivationReadbackSnapshot> ReadCurrentAsync(
        NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority,
        IReadOnlyList<NodeActivationIntent> intents,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ValidateRequestBinding(identity, authority, apiKey);
        var uri = new Uri(managementOrigin, "/api/node/v1/activation/current");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureExpectedTarget(response, uri);
        if (response.StatusCode != HttpStatusCode.OK)
            throw Failure();
        var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
        var readback = NodeActivationContractParser.ParseReadback(bytes);
        NodeActivationContractParser.ValidateReadbackBinding(readback, identity, authority, intents);
        PersistReadbackReceipt(readback, identity, authority, intents);
        return new NodeActivationReadbackSnapshot(readback, bytes,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public async Task<NodeActivationReceipt> SendActivationAsync(
        NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority,
        NodeActivationIntent intent,
        IReadOnlyList<NodeActivationIntent> intents,
        NodeActivationReadbackSnapshot freshReadback,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ValidateRequestBinding(identity, authority, apiKey);
        ValidateFreshPrecondition(identity, authority, intent, intents, freshReadback);
        if (intent.State is not ("Prepared" or "ResponseUnknown") || !NodeActivationContractParser.TryDecodeBase64(intent.RequestJsonBase64, out var body) ||
            body.Length > MaximumResponseBytes || !NodeActivationContractParser.DigestMatches(body, intent.RequestSha256))
            throw Failure();
        var requestDocument = NodeActivationContractParser.ParseRequest(body, identity, authority, intent);
        if (requestDocument.ActivationRequestId != intent.ActivationRequestId)
            throw Failure();

        var uri = new Uri(managementOrigin, "/api/node/v1/activation");
        var durableIntent = intent.State == "Prepared"
            ? intentLedger.MarkResponseUnknown(intent.ActivationRequestId)
            : intentLedger.GetIntent(intent.ActivationRequestId);
        if (durableIntent.State != "ResponseUnknown" || durableIntent.RequestSha256 != intent.RequestSha256 ||
            durableIntent.RequestJsonBase64 != intent.RequestJsonBase64 || durableIntent.ExpectedIncarnation != intent.ExpectedIncarnation ||
            durableIntent.RecoveryEpoch != intent.RecoveryEpoch || durableIntent.ProcessStartId != intent.ProcessStartId)
            throw Failure();
        return await SendWithRetriesAsync(uri, apiKey, body, identity, authority, durableIntent, requestDocument,
            cancellationToken);
    }

    private async Task<NodeActivationReceipt> SendWithRetriesAsync(Uri uri, string apiKey, byte[] body,
        NodeIdentityManifest identity, NodeExternalAuthorityEvidence authority, NodeActivationIntent intent,
        NodeActivationRequestDocument requestDocument, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumTransportAttempts; attempt++)
        {
            try
            {
                var receipt = await SendOnceAsync(uri, apiKey, body, identity, authority, intent, requestDocument,
                    cancellationToken);
                return receipt;
            }
            catch (Exception exception) when (ShouldRetry(exception, attempt, cancellationToken))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt)), cancellationToken);
            }
        }
        throw Failure();
    }

    private async Task<NodeActivationReceipt> SendOnceAsync(Uri uri, string apiKey, byte[] body,
        NodeIdentityManifest identity, NodeExternalAuthorityEvidence authority, NodeActivationIntent intent,
        NodeActivationRequestDocument requestDocument, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, uri, apiKey);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureExpectedTarget(response, uri);
        if (IsRetryable(response.StatusCode))
            throw new NodeActivationRetryableException();
        if (response.StatusCode != HttpStatusCode.OK)
            throw Failure();
        var receiptBytes = await ReadBoundedAsync(response.Content, cancellationToken);
        var receipt = NodeActivationContractParser.ParseReceipt(receiptBytes, identity, authority, intent, requestDocument);
        _ = intentLedger.PersistReceipt(intent.ActivationRequestId, receipt, receiptBytes);
        return receipt;
    }

    private void PersistReadbackReceipt(NodeActivationCurrentReadback readback, NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority, IReadOnlyList<NodeActivationIntent> intents)
    {
        if (readback.CurrentActivationRequestId is not Guid requestId)
            return;
        var intent = intents.Single(value => value.ActivationRequestId == requestId);
        if (intent.State != "ResponseUnknown")
            return;
        if (!NodeIdentityManifest.TryPositive(intent.RecoveryEpoch, out var intentEpoch) ||
            !NodeActivationContractParser.TryDecodeBase64(intent.RequestJsonBase64, out var requestBytes) ||
            !NodeActivationContractParser.TryDecodeBase64(readback.ActivationReceiptJsonBase64 ?? string.Empty, out var receiptBytes))
            throw Failure();
        var intentAuthority = authority with { RecoveryEpoch = intentEpoch };
        var request = NodeActivationContractParser.ParseRequest(requestBytes, identity, intentAuthority, intent);
        var receipt = NodeActivationContractParser.ParseReceipt(receiptBytes, identity, intentAuthority, intent, request);
        _ = intentLedger.PersistReceipt(requestId, receipt, receiptBytes);
    }

    private static void ValidateFreshPrecondition(NodeIdentityManifest identity, NodeExternalAuthorityEvidence authority,
        NodeActivationIntent intent, IReadOnlyList<NodeActivationIntent> intents, NodeActivationReadbackSnapshot snapshot)
    {
        var parsed = NodeActivationContractParser.ParseReadback(snapshot.ExactResponseJson);
        if (parsed != snapshot.Readback || !NodeActivationContractParser.DigestMatches(snapshot.ExactResponseJson, snapshot.ResponseSha256))
            throw Failure();
        NodeActivationContractParser.ValidateReadbackBinding(parsed, identity, authority, intents);
        if (parsed.CurrentActivationRequestId == intent.ActivationRequestId || !HasSamePrecondition(parsed, authority, intent) ||
            HasDifferentUnresolvedPrecondition(intents, intent))
            throw Failure();
    }

    private static bool HasSamePrecondition(NodeActivationCurrentReadback readback,
        NodeExternalAuthorityEvidence authority, NodeActivationIntent intent)
    {
        if (intent.ExpectedIncarnation == "0")
            return (readback.Status is "NoActivation" or "Enrolled") && readback.CurrentIncarnation is null &&
                readback.AuthoritativeRecoveryEpoch == authority.RecoveryEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (readback.Status is "Recovering" or "Quiescing" or "Fenced") &&
            readback.CurrentIncarnation == intent.ExpectedIncarnation;
    }

    private static bool HasDifferentUnresolvedPrecondition(IReadOnlyList<NodeActivationIntent> intents,
        NodeActivationIntent current) => intents.Any(intent => intent.ActivationRequestId != current.ActivationRequestId &&
            intent.State == "ResponseUnknown" &&
            (intent.ExpectedIncarnation != current.ExpectedIncarnation || intent.RecoveryEpoch != current.RecoveryEpoch));

    private static bool ShouldRetry(Exception exception, int attempt, CancellationToken cancellationToken) =>
        attempt + 1 < MaximumTransportAttempts && !cancellationToken.IsCancellationRequested &&
        exception is HttpRequestException or NodeActivationRetryableException or TaskCanceledException;

    internal static bool TryCreateManagementOrigin(string? value, out Uri? origin)
    {
        origin = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) || parsed.AbsolutePath is not ("/" or ""))
            return false;
        origin = new UriBuilder(parsed) { Path = string.Empty }.Uri;
        return true;
    }

    private static Uri ParseOrigin(string? value) =>
        TryCreateManagementOrigin(value, out var origin) ? origin! : throw Failure();

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!request.Headers.TryAddWithoutValidation("X-API-Key", apiKey))
            throw Failure();
        return request;
    }

    private static void ValidateRequestBinding(NodeIdentityManifest identity, NodeExternalAuthorityEvidence authority,
        string apiKey)
    {
        if (identity.NodeId == Guid.Empty || identity.InstallationId == Guid.Empty || identity.ControlPlaneRealmId == Guid.Empty ||
            !NodeIdentityManifest.TryPositive(identity.CredentialRevision, out _) ||
            authority.ControlPlaneRealmId != identity.ControlPlaneRealmId || authority.RecoveryEpoch <= 0 ||
            string.IsNullOrWhiteSpace(authority.AuthorityRevision) || authority.AuthorityRevision.Length > 128 ||
            !LowerSha256.IsMatch(authority.AttestationSha256) || apiKey.Length is 0 or > 256 ||
            apiKey.Any(character => character < 0x21 || character > 0x7E))
            throw Failure();
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
            throw Failure();
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var bytes = new byte[MaximumResponseBytes + 1];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(total), cancellationToken);
            if (read == 0)
                break;
            total += read;
        }
        if (total is 0 or > MaximumResponseBytes)
            throw Failure();
        return bytes.AsSpan(0, total).ToArray();
    }

    private static void EnsureExpectedTarget(HttpResponseMessage response, Uri requestedUri)
    {
        if (response.RequestMessage?.RequestUri != requestedUri)
            throw Failure();
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static InvalidDataException Failure() => new("Node activation endpoint is unavailable or returned an invalid contract.");
}

internal sealed class NodeActivationRetryableException : HttpRequestException { }

internal interface INodeActivationIntentLedger
{
    NodeActivationIntent MarkResponseUnknown(Guid requestId);
    NodeActivationIntent GetIntent(Guid requestId);
    NodeActivationIntent PersistReceipt(Guid requestId, NodeActivationReceipt receipt, ReadOnlySpan<byte> exactReceipt);
}

internal sealed record NodeExternalAuthorityEvidence(
    Guid ControlPlaneRealmId,
    long RecoveryEpoch,
    string AuthorityRevision,
    string AttestationSha256);

internal sealed record NodeActivationReadbackSnapshot(
    NodeActivationCurrentReadback Readback,
    byte[] ExactResponseJson,
    string ResponseSha256);

internal sealed record NodeActivationRequestDocument(
    int SchemaVersion,
    Guid ControlPlaneRealmId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RecoveryEpoch,
    Guid NodeId,
    Guid InstallationId,
    Guid ActivationRequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedIncarnation,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long CredentialRevision,
    string JournalInventorySha256,
    string RecoveryCheckpointSha256,
    IReadOnlyList<int> SupportedManagementSchemaVersions,
    JsonElement? HostFenceReceipt);

internal sealed record NodeActivationReceipt(
    int SchemaVersion,
    Guid NodeId,
    Guid InstallationId,
    Guid ControlPlaneRealmId,
    Guid ActivationRequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Incarnation,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RecoveryEpoch,
    string RecoveryCheckpointSha256,
    string Status,
    DateTime CreatedAtUtc);

internal sealed record NodeActivationCurrentReadback(
    int SchemaVersion,
    Guid ControlPlaneRealmId,
    Guid NodeId,
    Guid InstallationId,
    string CredentialRevision,
    string? RecoveryEpoch,
    string AuthoritativeRecoveryEpoch,
    string Status,
    string? CurrentIncarnation,
    Guid? CurrentActivationRequestId,
    string? RequestSha256,
    string? ActivationReceiptJsonBase64);

