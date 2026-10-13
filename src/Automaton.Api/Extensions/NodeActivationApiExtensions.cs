using Automaton.Api.Services;

namespace Automaton.Api.Extensions;

internal static class NodeActivationApiExtensions
{
    public static WebApplication MapNodeActivationEndpoint(this WebApplication app)
    {
        app.MapPost("/api/node/v1/activation", HandleActivationAsync);
        return app;
    }

    private static async Task<IResult> HandleActivationAsync(HttpContext context, NodeEnrollmentService enrollment,
        NodeActivationService activation, CancellationToken cancellationToken)
    {
        if (!TryReadApiKey(context.Request.Headers, out var apiKey))
            return Results.Unauthorized();
        var identity = await enrollment.AuthenticateManagementRequestAsync(context, apiKey, cancellationToken);
        if (!identity.Accepted)
            return Results.Unauthorized();
        var body = await ReadBoundedBodyAsync(context.Request.Body, cancellationToken);
        if (body is null || !NodeActivationRequestParser.TryParse(body.Value, out var request, out var canonicalBytes))
            return Results.BadRequest();
        return ToResult(await activation.ActivateAsync(identity, request!, canonicalBytes, cancellationToken));
    }

    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        const int maximumLength = 64 * 1024;
        var bytes = new byte[maximumLength + 1];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(total, bytes.Length - total), cancellationToken);
            if (read == 0)
                break;
            total += read;
        }
        return total is 0 or > maximumLength ? null : bytes.AsMemory(0, total);
    }

    private static bool TryReadApiKey(IHeaderDictionary headers, out string apiKey)
    {
        var values = headers["X-API-Key"];
        apiKey = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        return values.Count == 1 && apiKey.Length is > 0 and <= 256;
    }

    private static IResult ToResult(NodeActivationResult result) => result.Disposition switch
    {
        NodeActivationDisposition.Accepted => Results.Bytes(result.ImmutableReceiptJson!, "application/json"),
        NodeActivationDisposition.Invalid => Results.BadRequest(new { code = result.ErrorCode }),
        NodeActivationDisposition.Unavailable => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        _ => Results.Conflict(new { code = result.ErrorCode })
    };
}
