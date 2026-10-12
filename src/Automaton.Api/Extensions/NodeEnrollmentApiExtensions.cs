using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Automaton.Api.Extensions;

internal static class NodeEnrollmentServiceRegistration
{
    public static IServiceCollection AddNodeEnrollmentServices(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<NodeEnrollmentService>();
        return services;
    }
}

internal static class NodeEnrollmentEndpointRegistration
{
    public static WebApplication MapNodeEnrollmentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/node-enrollments/{enrollmentId:guid}/bootstrap",
            async (Guid enrollmentId, NodeBootstrapRequest request, NodeEnrollmentService enrollment, CancellationToken cancellationToken) =>
                ToHttpResult(await enrollment.BootstrapAsync(enrollmentId, request, cancellationToken)));

        app.MapPost("/api/node-enrollments/{enrollmentId:guid}/recovery/challenge",
            async (Guid enrollmentId, NodeRecoveryChallengeRequest request, NodeEnrollmentService enrollment, CancellationToken cancellationToken) =>
            {
                var result = await enrollment.CreateRecoveryChallengeAsync(
                    enrollmentId,
                    request.InstallationId,
                    request.ControlPlaneRealmId,
                    cancellationToken);
                return result.Accepted ? Results.Ok(result) : Results.NotFound();
            });

        app.MapPost("/api/node-enrollments/{enrollmentId:guid}/recovery/complete",
            async (Guid enrollmentId, NodeRecoveryCompletionRequest request, NodeEnrollmentService enrollment, CancellationToken cancellationToken) =>
                ToHttpResult(await enrollment.CompleteRecoveryAsync(enrollmentId, request, cancellationToken)));

        app.MapGet("/api/node/v1/identity",
            async (HttpContext context, NodeEnrollmentService enrollment, CancellationToken cancellationToken) =>
            {
                var keys = context.Request.Headers["X-API-Key"];
                if (keys.Count != 1)
                    return Results.Unauthorized();
                var result = await enrollment.AuthenticateManagementRequestAsync(
                    keys[0], context.Request.Headers, cancellationToken);
                return result.Accepted ? Results.Ok(result) : Results.Unauthorized();
            });
        return app;
    }

    private static IResult ToHttpResult(NodeEnrollmentOperationResult result)
    {
        if (result.InvalidInput)
            return Results.BadRequest();
        if (!result.Accepted)
            return Results.Conflict();
        return Results.Ok(new NodeEnrollmentResponse(result.Receipt!, result.Credentials));
    }
}

public sealed record NodeRecoveryChallengeRequest(Guid InstallationId, Guid ControlPlaneRealmId);
public sealed record NodeEnrollmentResponse(NodeEnrollmentReceipt Receipt, NodeEnrollmentCredentialBundle? Credentials);
