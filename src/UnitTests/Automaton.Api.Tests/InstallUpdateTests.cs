using System.Security.Claims;
using System.Text.Json;
using Automaton.Api.Controllers;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InstallUpdateTests
{
    private string root = null!;
    private string requests = null!;
    private string status = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "automaton-update-" + Guid.NewGuid().ToString("N"));
        requests = Path.Combine(root, "requests");
        status = Path.Combine(root, "status");
        Directory.CreateDirectory(requests);
        Directory.CreateDirectory(status);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, recursive: true);

    [Test]
    public async Task PostWritesTheRequestAndRecordsWhoAskedAndAudits()
    {
        var audit = new RecordingAudit();
        var userId = Guid.NewGuid();

        var result = await Controller(audit, userId, "admin@example.com").Post(new("server-v0.2.0"), default);

        Assert.That(result, Is.InstanceOf<AcceptedResult>());
        using var request = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(requests, "request.json")));
        Assert.That(request.RootElement.GetProperty("version").GetString(), Is.EqualTo("server-v0.2.0"));
        Assert.That(request.RootElement.GetProperty("requestedBy").GetString(), Is.EqualTo($"admin@example.com {userId}"));
        Assert.That(Directory.GetFiles(requests, ".request-*"), Is.Empty);
        Assert.That(audit.Requests.Single().ActorUserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task PostDefaultsToLatest()
    {
        await Controller(new RecordingAudit(), Guid.NewGuid(), null).Post(null, default);

        using var request = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(requests, "request.json")));
        Assert.That(request.RootElement.GetProperty("version").GetString(), Is.EqualTo("latest"));
    }

    [TestCase("1.2.3")]
    [TestCase("server-v1.2")]
    [TestCase("server-v1.2.3; rm -rf /")]
    [TestCase("server-v1.2.3-beta.1")]
    [TestCase("../latest")]
    public async Task PostRejectsAnythingButLatestOrAReleaseTag(string version)
    {
        var result = await Controller(new RecordingAudit(), Guid.NewGuid(), null).Post(new(version), default);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(File.Exists(Path.Combine(requests, "request.json")), Is.False);
    }

    [Test]
    public async Task PostReturnsConflictWhileAnUpdateRuns()
    {
        await File.WriteAllTextAsync(Path.Combine(status, "status.json"),
            JsonSerializer.Serialize(new { state = "running", startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) }));

        var result = await Controller(new RecordingAudit(), Guid.NewGuid(), null).Post(new("latest"), default);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        Assert.That(File.Exists(Path.Combine(requests, "request.json")), Is.False);
    }

    [Test]
    public async Task PostReturnsConflictWhenARequestIsAlreadyPending()
    {
        await File.WriteAllTextAsync(Path.Combine(requests, "request.json"), "{}");

        var result = await Controller(new RecordingAudit(), Guid.NewGuid(), null).Post(new("latest"), default);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
    }

    [Test]
    public async Task PostIgnoresAStaleRunningStatusFromAnInterruptedUpdate()
    {
        await File.WriteAllTextAsync(Path.Combine(status, "status.json"),
            JsonSerializer.Serialize(new { state = "running", startedAtUtc = DateTimeOffset.UtcNow.AddHours(-4) }));

        var result = await Controller(new RecordingAudit(), Guid.NewGuid(), null).Post(new("latest"), default);

        Assert.That(result, Is.InstanceOf<AcceptedResult>());
    }

    [Test]
    public async Task PostReturns503WithGuidanceWhenTheTriggerDirectoryIsMissing()
    {
        Directory.Delete(requests);

        var result = await Controller(new RecordingAudit(), Guid.NewGuid(), null).Post(new("latest"), default);

        var response = (ObjectResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(JsonSerializer.Serialize(response.Value), Does.Contain("update.sh"));
    }

    [Test]
    public async Task GetReportsTheCurrentVersionStatusAndLog()
    {
        await File.WriteAllTextAsync(Path.Combine(status, "status.json"), """
            {"state":"failed","requestedVersion":"latest","fromVersion":"server-v0.1.1","toVersion":null,
             "startedAtUtc":"2026-11-04T10:00:00Z","finishedAtUtc":"2026-11-04T10:05:00Z","exitCode":1,
             "recoveryBackup":"/opt/automaton-server/backups/x","requestedBy":"admin@example.com","message":"m"}
            """);
        await File.WriteAllTextAsync(Path.Combine(status, "update.log"), "line one\nline two\n");

        var result = (await Controller(new RecordingAudit(), Guid.NewGuid(), null).Get(default)).Result as OkObjectResult;

        var body = (InstallationUpdateStatus)result!.Value!;
        Assert.That(body.CurrentVersion, Is.EqualTo("server-v0.1.1"));
        Assert.That(body.TriggerAvailable, Is.True);
        Assert.That(body.Run!.State, Is.EqualTo("failed"));
        Assert.That(body.Run.ExitCode, Is.EqualTo(1));
        Assert.That(body.Run.RecoveryBackup, Is.EqualTo("/opt/automaton-server/backups/x"));
        Assert.That(body.Log, Does.Contain("line two"));
    }

    [Test]
    public async Task GetWithoutAnyRunHasNoStatus()
    {
        var result = (await Controller(new RecordingAudit(), Guid.NewGuid(), null).Get(default)).Result as OkObjectResult;

        var body = (InstallationUpdateStatus)result!.Value!;
        Assert.That(body.Run, Is.Null);
        Assert.That(body.Log, Is.Null);
    }

    [Test]
    public async Task GetReportsAnUnreadableStatusFileInsteadOfFailing()
    {
        await File.WriteAllTextAsync(Path.Combine(status, "status.json"), "{ not json");

        var result = (await Controller(new RecordingAudit(), Guid.NewGuid(), null).Get(default)).Result as OkObjectResult;

        Assert.That(((InstallationUpdateStatus)result!.Value!).Run!.State, Is.EqualTo("unknown"));
    }

    private InstallUpdateController Controller(RecordingAudit audit, Guid userId, string? email)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Installation:Version"] = "server-v0.1.1",
            ["Installation:UpdateRequestsDirectory"] = requests,
            ["Installation:UpdateStatusDirectory"] = status
        }).Build();
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId.ToString())];
        if (email is not null) claims.Add(new(ClaimTypes.Email, email));
        return new InstallUpdateController(configuration, audit)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } }
        };
    }

    private sealed class RecordingAudit : IAuditEventService
    {
        public List<AuditEventWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
