using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static Automaton.Api.Tests.InfrastructureWorkflowExecutionServiceTests;

namespace Automaton.Api.Tests;

public sealed class InfrastructureHostValidationControllerTests
{
    [TestCase(false, false, typeof(OkObjectResult))]
    [TestCase(true, false, typeof(ForbidResult))]
    [TestCase(false, true, typeof(BadRequestObjectResult))]
    public async Task ResultMapsIdentityAndMalformedPayload(bool wrongKey, bool invalidPayload, Type expectedType)
    {
        await using var db = CreateDbContext();
        var host = await SeedValidationHostAsync(db);
        var publisher = new RecordingPublisher();
        await CreateWorkflowExecutionService(db, publisher).StartHostValidationAsync(
            new() { RunnerHostId = host.Id }, Guid.NewGuid(), CancellationToken.None);
        var command = publisher.LastCommand!;
        var request = Result(command);
        if (invalidPayload) request = request with { Result = new(true, "[]", null) };
        var controller = CreateController(db,
            wrongKey ? Guid.NewGuid() : host.InfrastructureRunner!.ApiKeyId!.Value);
        var response = await controller.Result(command.CommandId, request, CancellationToken.None);
        Assert.That(response.Result, Is.InstanceOf(expectedType));
    }

    private static InfrastructureCommandsController CreateController(ApplicationDbContext db,
        Guid apiKeyId)
        => new(null!, new InfrastructureHostValidationResultService(db, new RecordingAuditEventService()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("api_key_id", apiKeyId.ToString())]))
                }
            }
        };
}
