using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostContainerRelayServiceTests
{
    private static readonly IReadOnlyDictionary<string, AuthorizationFailureCase> AuthorizationFailures =
        new Dictionary<string, AuthorizationFailureCase>
        {
            ["stale"] = new("caller_not_managed_runner", (fixture, caller, request) => { caller.ConnectionId = "stale"; return request; }),
            ["profile"] = new("caller_not_managed_runner", (fixture, caller, request) => { fixture.HostedRunnerEntity.ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost; return request; }),
            ["purpose"] = new("caller_not_managed_runner", (fixture, caller, request) => { caller.Purpose = RunnerPurposes.Infrastructure; return request; }),
            ["hosted"] = new("hosted_runner_binding_mismatch", (fixture, caller, request) => { fixture.HostedRunner.DeletedAt = DateTime.UtcNow; return request; }),
            ["host"] = new("runner_host_binding_mismatch", (fixture, caller, request) => { caller.RunnerHostId = Guid.NewGuid(); return request; }),
            ["scope-hosted"] = new("hosted_runner_binding_mismatch", (fixture, caller, request) => request with { Scope = request.Scope with { HostedRunnerId = Guid.NewGuid() } }),
            ["scope-host"] = new("runner_host_binding_mismatch", (fixture, caller, request) => request with { Scope = request.Scope with { RunnerHostId = Guid.NewGuid() } }),
            ["target"] = new("deployment_target_mismatch", (fixture, caller, request) => request with { Scope = request.Scope with { DeploymentTargetId = Guid.NewGuid() } }),
            ["app"] = new("deployment_target_mismatch", (fixture, caller, request) => request with { Scope = request.Scope with { AppId = Guid.NewGuid() } }),
            ["assignment"] = new("app_assignment_mismatch", (fixture, caller, request) => { fixture.Target.ExecutionRunnerId = null; return request; }),
            ["infra"] = new("infrastructure_runner_missing", (fixture, caller, request) => { fixture.Host.InfrastructureRunnerId = null; return request; }),
            ["offline"] = new("infrastructure_runner_offline", (fixture, caller, request) => { fixture.Infrastructure.LastHeartbeatAtUtc = DateTime.UtcNow.AddHours(-1); return request; }),
            ["protocol"] = new("host_container_protocol_unsupported", (fixture, caller, request) => { fixture.Infrastructure.ApiCompatibilityVersion = "3"; return request; }),
            ["capability"] = new("host_container_capability_missing", (fixture, caller, request) => { fixture.Infrastructure.CapabilitiesJson = "[\"ApplicationHosting\"]"; return request; }),
            ["api"] = new("infrastructure_runner_wrong_api_instance", (fixture, caller, request) => { fixture.Infrastructure.ApiInstanceId = "other"; return request; })
        };

    [Test]
    public async Task ResolveImage_BindsPersistedStandardHostedRunnerAndForwardsTrustedScope()
    {
        using var fixture = RelayFixture.Create();
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);

        Assert.That(result.Success, Is.True, $"{result.ErrorCode}: {result.Error}");
        Assert.That(fixture.Transport.ConnectionId, Is.EqualTo("infra-connection"));
        Assert.That(fixture.Transport.Method, Is.EqualTo(AutomatonHubMethods.ExecuteHostContainer));
        Assert.That(fixture.Transport.Request!.Scope, Is.EqualTo(new HostContainerScope(
            fixture.HostedRunner.Id, fixture.Host.Id, fixture.App.Id, fixture.Target.Id)));
        Assert.That(fixture.Transport.Request.RequestedImage, Is.EqualTo("python:3.13-slim"));
        Assert.That(fixture.Transport.Request.Resources, Is.Null);
        Assert.That(fixture.Transport.Request.Command, Is.Null);
        Assert.That(fixture.Transport.Request.ContainerId, Is.Null);
        Assert.That(fixture.Transport.Request.HostPort, Is.Zero);
        Assert.That(fixture.Transport.Request.ContainerPort, Is.Zero);
        Assert.That(fixture.Transport.Request.Environment, Is.Empty);
    }

    [TestCase(RunnerPurposes.Infrastructure, RunnerApplicationProfiles.Standard, "caller_not_managed_runner")]
    [TestCase(RunnerPurposes.Managed, RunnerApplicationProfiles.PrivilegedHost, "caller_not_managed_runner")]
    [TestCase(RunnerPurposes.Managed, "Unknown", "caller_not_managed_runner")]
    public async Task ResolveImage_UsesPersistedCallerProfile(string purpose, string? profile, string code)
    {
        using var fixture = RelayFixture.Create();
        fixture.HostedRunner.Runner!.Purpose = purpose;
        fixture.HostedRunner.Runner.ApplicationProfile = profile;
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo(code));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_RejectsMissingOrWrongRunnerHostIdentity()
    {
        using var fixture = RelayFixture.Create();
        var missing = fixture.Caller();
        missing.RunnerHostId = null;
        Assert.That((await fixture.Service.ExecuteAsync(missing, fixture.Request(), CancellationToken.None)).ErrorCode,
            Is.EqualTo("runner_host_binding_mismatch"));

        var wrong = fixture.Caller();
        wrong.RunnerHostId = Guid.NewGuid();
        Assert.That((await fixture.Service.ExecuteAsync(wrong, fixture.Request(), CancellationToken.None)).ErrorCode,
            Is.EqualTo("runner_host_binding_mismatch"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_RejectsStaleCallerConnection()
    {
        using var fixture = RelayFixture.Create();
        var caller = fixture.Caller();
        caller.ConnectionId = "stale-connection";
        var result = await fixture.Service.ExecuteAsync(caller, fixture.Request(), CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("caller_not_managed_runner"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_AcceptsLegacyNullProfileAsStandard()
    {
        using var fixture = RelayFixture.Create();
        fixture.HostedRunnerEntity.ApplicationProfile = null;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);

        Assert.That(result.Success, Is.True, $"{result.ErrorCode}: {result.Error}");
        Assert.That(fixture.Transport.Invocations, Is.EqualTo(1));
    }

    [Test]
    public async Task ResolveImage_AcceptsNewAuthoritativeConnectionAfterReconnect_AndRejectsOldOne()
    {
        using var fixture = RelayFixture.Create();
        fixture.HostedRunnerEntity.ConnectionId = "new-connection";
        await fixture.Db.SaveChangesAsync();

        var stale = fixture.Caller();
        stale.ConnectionId = "old-connection";
        var staleResult = await fixture.Service.ExecuteAsync(stale, fixture.Request(), CancellationToken.None);
        Assert.That(staleResult.ErrorCode, Is.EqualTo("caller_not_managed_runner"));

        var current = fixture.Caller();
        current.ConnectionId = "new-connection";
        var currentResult = await fixture.Service.ExecuteAsync(current, fixture.Request(), CancellationToken.None);
        Assert.That(currentResult.Success, Is.True, $"{currentResult.ErrorCode}: {currentResult.Error}");
        Assert.That(fixture.Transport.Invocations, Is.EqualTo(1));
    }

    [Test]
    public async Task ResolveImage_RejectsDifferentManagedRunner()
    {
        using var fixture = RelayFixture.Create();
        var caller = fixture.Caller();
        caller.Id = Guid.NewGuid();

        var result = await fixture.Service.ExecuteAsync(caller, fixture.Request(), CancellationToken.None);

        Assert.That(result.ErrorCode, Is.EqualTo("caller_not_managed_runner"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    public async Task ResolveImage_RejectsInvalidHostedRunnerAssociation(bool missing, bool deleted, bool pointsElsewhere)
    {
        using var fixture = RelayFixture.Create();
        if (missing) fixture.Db.HostedRunners.Remove(fixture.HostedRunner);
        if (deleted) fixture.HostedRunner.DeletedAt = DateTime.UtcNow;
        if (pointsElsewhere) fixture.HostedRunner.RunnerId = Guid.NewGuid();
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("hosted_runner_binding_mismatch"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [TestCase(false, "deployment_target_mismatch")]
    [TestCase(true, "app_assignment_mismatch")]
    public async Task ResolveImage_RejectsMissingOrUnassignedTarget(bool unassigned, string expectedCode)
    {
        using var fixture = RelayFixture.Create();
        var request = fixture.Request() with
        {
            Scope = new HostContainerScope(Guid.Empty, Guid.Empty, fixture.App.Id,
                unassigned ? fixture.Target.Id : Guid.NewGuid())
        };
        if (unassigned)
        {
            fixture.Target.ExecutionRunnerId = null;
            await fixture.Db.SaveChangesAsync();
        }
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), request, CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo(expectedCode));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_RejectsTargetBelongingToAnotherApp()
    {
        using var fixture = RelayFixture.Create();
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request() with
        {
            Scope = new HostContainerScope(Guid.Empty, Guid.Empty, Guid.NewGuid(), fixture.Target.Id)
        }, CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("deployment_target_mismatch"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [TestCase(false, "infrastructure_runner_missing")]
    [TestCase(true, "infrastructure_runner_offline")]
    [TestCase(true, "host_container_capability_missing")]
    public async Task ResolveImage_RejectsUnavailableInfrastructureRunner(bool configured, string expectedCode)
    {
        using var fixture = RelayFixture.Create();
        if (!configured) fixture.Host.InfrastructureRunnerId = null;
        else if (expectedCode == "infrastructure_runner_offline") fixture.Infrastructure.LastHeartbeatAtUtc = DateTime.UtcNow.AddMinutes(-10);
        else fixture.Infrastructure.CapabilitiesJson = "[\"ApplicationHosting\"]";
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo(expectedCode));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_ReturnsStableFailureWhenTransportThrows()
    {
        using var fixture = RelayFixture.Create();
        fixture.Transport.Throw = true;
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("infrastructure_transport_failure"));
        Assert.That(fixture.Transport.Invocations, Is.EqualTo(1));
    }

    [Test]
    public async Task ResolveImage_RejectsCallerSuppliedRunnerHostScope()
    {
        using var fixture = RelayFixture.Create();
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request() with
        {
            Scope = new HostContainerScope(Guid.Empty, Guid.NewGuid(), fixture.App.Id, fixture.Target.Id)
        }, CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("runner_host_binding_mismatch"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [TestCase("resources")]
    [TestCase("container")]
    [TestCase("command")]
    [TestCase("host-port")]
    [TestCase("container-port")]
    [TestCase("environment")]
    public async Task ResolveImage_RejectsExecutionOnlyInputs(string shape)
    {
        using var fixture = RelayFixture.Create();
        var request = fixture.Request();
        request = shape switch
        {
            "resources" => request with { Resources = new HostContainerResourceRef("w", "a", "d") },
            "container" => request with { ContainerId = "container" },
            "command" => request with { Command = new ApplicationCommandSpec { Executable = "run" } },
            "host-port" => request with { HostPort = 8080 },
            "container-port" => request with { ContainerPort = 8080 },
            _ => request with { Environment = new Dictionary<string, string> { ["X"] = "Y" } }
        };
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), request, CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("unsupported_operation"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_RejectsWrongScopeAndMalformedShape()
    {
        using var fixture = RelayFixture.Create();
        var wrongHost = fixture.Request() with { Scope = new HostContainerScope(Guid.NewGuid(), fixture.Host.Id, fixture.App.Id, fixture.Target.Id) };
        Assert.That((await fixture.Service.ExecuteAsync(fixture.Caller(), wrongHost, CancellationToken.None)).ErrorCode,
            Is.EqualTo("hosted_runner_binding_mismatch"));

        var wrongOperation = fixture.Request() with { Operation = HostContainerOperationKinds.RunBuild };
        Assert.That((await fixture.Service.ExecuteAsync(fixture.Caller(), wrongOperation, CancellationToken.None)).ErrorCode,
            Is.EqualTo("invalid_request"));

        var emptyImage = fixture.Request() with { RequestedImage = " " };
        Assert.That((await fixture.Service.ExecuteAsync(fixture.Caller(), emptyImage, CancellationToken.None)).ErrorCode,
            Is.EqualTo("invalid_request"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_RejectsNonCurrentInfrastructureApiInstance()
    {
        using var fixture = RelayFixture.Create();
        fixture.Infrastructure.ApiInstanceId = "other-api";
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("infrastructure_runner_wrong_api_instance"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task ResolveImage_RejectsUnsupportedInfrastructureRunner()
    {
        using var fixture = RelayFixture.Create();
        fixture.Infrastructure.ApiCompatibilityVersion = "3";
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), fixture.Request(), CancellationToken.None);
        Assert.That(result.ErrorCode, Is.EqualTo("host_container_protocol_unsupported"));
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    [Test]
    public async Task RunBuildDerivesTrustedResourceKindsAndPreservesBuildSemantics()
    {
        using var fixture = RelayFixture.Create();
        var attemptId = Guid.NewGuid();
        fixture.Target.ActiveDeploymentExecutionAttemptId = attemptId;
        var result = await fixture.Service.ExecuteAsync(
            fixture.Caller(), CreateBuildRequest(fixture, attemptId), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(fixture.Transport.Request, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Transport.Request!.Resources!.SourceKind, Is.EqualTo(HostContainerResourceKinds.Source));
            Assert.That(fixture.Transport.Request.Resources.ArtifactKind, Is.EqualTo(HostContainerResourceKinds.Artifact));
            Assert.That(fixture.Transport.Request.Resources.DataKind, Is.EqualTo(HostContainerResourceKinds.ApplicationData));
            Assert.That(fixture.Transport.Request.Command!.Executable, Is.EqualTo("dotnet"));
            Assert.That(fixture.Transport.Request.Environment["BUILD_MODE"], Is.EqualTo("release"));
        });
    }

    private static HostContainerExecutionRequest CreateBuildRequest(RelayFixture fixture, Guid attemptId)
        => new()
        {
            Operation = HostContainerOperationKinds.RunBuild,
            RequestedImage = "dotnet/sdk:9.0",
            Command = new ApplicationCommandSpec { Executable = "dotnet", Arguments = ["publish"] },
            Environment = new Dictionary<string, string> { ["BUILD_MODE"] = "release" },
            Scope = new HostContainerScope(Guid.Empty, Guid.Empty, fixture.App.Id, fixture.Target.Id, attemptId)
        };

    private static readonly string[] RuntimeOperations =
    [
        HostContainerOperationKinds.CreateApplicationContainer, HostContainerOperationKinds.StartContainer,
        HostContainerOperationKinds.InspectContainer, HostContainerOperationKinds.GetContainerLogs,
        HostContainerOperationKinds.RemoveContainer
    ];

    [TestCaseSource(nameof(RuntimeOperations))]
    public async Task RuntimeOperations_ForwardValidatedInputsAndResponse(string operation)
    {
        using var fixture = RelayFixture.Create();
        var request = fixture.RuntimeRequest(operation);
        fixture.Target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
        fixture.Transport.Result = new HostContainerExecutionResponse
        {
            Success = true, Exists = true, Running = true, ContainerId = "container",
            HostAddress = "10.0.0.2", HostPort = 32001, ContainerPort = 8080, Logs = "ready"
        };
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), request, CancellationToken.None);
        Assert.That(result, Is.SameAs(fixture.Transport.Result));
        var forwarded = fixture.Transport.Request!;
        Assert.Multiple(() =>
        {
            Assert.That(forwarded.Scope, Is.EqualTo(request.Scope with
            { HostedRunnerId = fixture.HostedRunner.Id, RunnerHostId = fixture.Host.Id }));
            Assert.That(forwarded.Operation, Is.EqualTo(operation));
            Assert.That(forwarded.ContainerId, Is.EqualTo(request.ContainerId));
            Assert.That(forwarded.CandidateId, Is.EqualTo(request.CandidateId));
            Assert.That(forwarded.Storage, Is.SameAs(request.Storage));
            Assert.That(forwarded.Command, Is.SameAs(request.Command));
            Assert.That(forwarded.Environment, Is.EquivalentTo(request.Environment));
            Assert.That(forwarded.RequestedImage, Is.EqualTo(request.RequestedImage));
            Assert.That(forwarded.ContainerPort, Is.EqualTo(request.ContainerPort));
            Assert.That(forwarded.HostPort, Is.Zero);
            Assert.That(forwarded.Resources, Is.EqualTo(operation == HostContainerOperationKinds.CreateApplicationContainer
                ? new HostContainerResourceRef("source", "artifact", "data") : null));
        });
    }

    [TestCaseSource(nameof(RuntimeOperations))]
    public async Task RuntimeOperations_PreserveAuthorizationMatrix(string operation)
    {
        foreach (var (failure, failureCase) in AuthorizationFailures)
        {
            using var fixture = RelayFixture.Create();
            var caller = fixture.Caller();
            var request = fixture.RuntimeRequest(operation);
            request = failureCase.Apply(fixture, caller, request);
            await fixture.Db.SaveChangesAsync();
            var result = await fixture.Service.ExecuteAsync(caller, request, CancellationToken.None);
            Assert.That(result.ErrorCode, Is.EqualTo(failureCase.ErrorCode), failure);
            Assert.That(fixture.Transport.Invocations, Is.Zero, failure);
        }
    }

    private sealed record AuthorizationFailureCase(
        string ErrorCode,
        Func<RelayFixture, RunnerDetails, HostContainerExecutionRequest, HostContainerExecutionRequest> Apply);

    [TestCaseSource(nameof(RuntimeOperations))]
    public async Task RuntimeOperations_RejectCallerResourcesAndHostPort(string operation)
    {
        using var fixture = RelayFixture.Create();
        var request = fixture.RuntimeRequest(operation);
        foreach (var invalid in new[]
        {
            request with { Resources = new HostContainerResourceRef("source", "artifact", "data") },
            request with { HostPort = 8080 },
            request with { ContainerId = "--unsafe" }
        })
        {
            var result = await fixture.Service.ExecuteAsync(fixture.Caller(), invalid, CancellationToken.None);
            Assert.That(result.ErrorCode, Is.EqualTo("invalid_request"));
        }
        Assert.That(fixture.Transport.Invocations, Is.Zero);

        if (operation == HostContainerOperationKinds.CreateApplicationContainer)
        {
            var mutable = request with { RequestedImage = "example/runtime:latest" };
            var accepted = await fixture.Service.ExecuteAsync(fixture.Caller(), mutable, CancellationToken.None);
            Assert.That(accepted.Success, Is.True, accepted.Error);
            Assert.That(fixture.Transport.Invocations, Is.EqualTo(1));
        }
    }

    [TestCaseSource(nameof(RuntimeOperations))]
    public async Task RuntimeOperations_DoNotRequireDeploymentAttempt(string operation)
    {
        using var fixture = RelayFixture.Create();
        var request = fixture.RuntimeRequest(operation);
        request = request with { Scope = request.Scope with { DeploymentAttemptId = Guid.Empty } };
        fixture.Target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
        var result = await fixture.Service.ExecuteAsync(fixture.Caller(), request, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error);
    }

    [TestCase(HostContainerOperationKinds.ResolveImage)]
    [TestCase(HostContainerOperationKinds.RunBuild)]
    public async Task BuildOperations_RejectRuntimeFields(string operation)
    {
        using var fixture = RelayFixture.Create();
        var request = fixture.Request() with { Operation = operation,
            Command = operation == HostContainerOperationKinds.RunBuild ? new ApplicationCommandSpec { Executable = "build" } : null };
        foreach (var invalid in new[] { request with { CandidateId = Guid.NewGuid() }, request with { Storage = new ApplicationStorageSpec() } })
        {
            var result = await fixture.Service.ExecuteAsync(fixture.Caller(), invalid, CancellationToken.None);
            Assert.That(result.Success, Is.False);
        }
        Assert.That(fixture.Transport.Invocations, Is.Zero);
    }

    private sealed class RelayFixture : IDisposable
    {
        public ApplicationDbContext Db { get; }
        public HostContainerRelayService Service { get; }
        public RecordingTransport Transport { get; } = new();
        public Runner HostedRunnerEntity { get; }
        public HostedRunner HostedRunner { get; }
        public RunnerHost Host { get; }
        public Runner Infrastructure { get; }
        public WebApp App { get; }
        public AppDeploymentTarget Target { get; }
        public Guid CallerRunnerId { get; } = Guid.NewGuid();
        private readonly string? previousApiInstanceId;

        private RelayFixture()
        {
            previousApiInstanceId = Environment.GetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID");
            Environment.SetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID", "api-current");
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            Host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = Guid.NewGuid() };
            Infrastructure = new Runner
            {
                Id = Host.InfrastructureRunnerId.Value, Name = "infra", Purpose = RunnerPurposes.Infrastructure,
                ConnectionId = "infra-connection", LastHeartbeatAtUtc = DateTime.UtcNow,
                ApiCompatibilityVersion = RunnerApiCompatibilityVersions.HostContainerExecution,
                CapabilitiesJson = "[\"HostContainerExecution\"]", ApiInstanceId = "api-current"
            };
            HostedRunnerEntity = new Runner
            {
                Id = CallerRunnerId, Name = "hosted", Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.Standard, ConnectionId = "hosted-connection",
                LastHeartbeatAtUtc = DateTime.UtcNow
            };
            HostedRunner = new HostedRunner { Id = Guid.NewGuid(), RunnerId = CallerRunnerId, Runner = HostedRunnerEntity, RunnerHostId = Host.Id, RunnerHost = Host, Name = "hosted", ContainerName = "hosted", UserId = Guid.NewGuid() };
            App = new WebApp { Id = Guid.NewGuid(), Name = "app", Summary = "", Description = "", Definition = "" };
            Target = AppDeploymentTarget.CreateLive(App.Id, CallerRunnerId);
            Db.RunnerHosts.Add(Host);
            Db.Runners.AddRange(HostedRunnerEntity, Infrastructure);
            Db.HostedRunners.Add(HostedRunner);
            Db.WebApps.Add(App);
            Db.AppDeploymentTargets.Add(Target);
            Db.SaveChanges();
            Transport.Result = new HostContainerExecutionResponse { Success = true, ResolvedImage = "python@sha256:test" };
            var stateResolver = new HostContainerRelayStateResolver(Db, new ApiInstanceIdentityService());
            Service = new HostContainerRelayService(stateResolver, Transport, NullLogger<HostContainerRelayService>.Instance);
        }

        public static RelayFixture Create() => new();
        public RunnerDetails Caller() => new() { Id = CallerRunnerId, ConnectionId = "hosted-connection", Purpose = RunnerPurposes.Managed, HostedRunnerId = HostedRunner.Id, RunnerHostId = Host.Id };
        public HostContainerExecutionRequest Request() => new() { Operation = HostContainerOperationKinds.ResolveImage, RequestedImage = "python:3.13-slim", Scope = new HostContainerScope(Guid.Empty, Guid.Empty, App.Id, Target.Id) };
        public HostContainerExecutionRequest RuntimeRequest(string operation) => new()
        {
            Operation = operation,
            Scope = new HostContainerScope(Guid.Empty, Guid.Empty, App.Id, Target.Id, Guid.NewGuid()),
            ContainerId = operation == HostContainerOperationKinds.CreateApplicationContainer ? null : "container",
            CandidateId = operation == HostContainerOperationKinds.CreateApplicationContainer ? Guid.NewGuid() : Guid.Empty,
            RequestedImage = operation == HostContainerOperationKinds.CreateApplicationContainer ? "repo@sha256:" + new string('a', 64) : null,
            Command = operation == HostContainerOperationKinds.CreateApplicationContainer ? new ApplicationCommandSpec { Executable = "serve" } : null,
            Storage = operation == HostContainerOperationKinds.CreateApplicationContainer ? new ApplicationStorageSpec() : null,
            ContainerPort = operation == HostContainerOperationKinds.CreateApplicationContainer ? 8080 : 0,
            Environment = operation == HostContainerOperationKinds.CreateApplicationContainer
                ? new Dictionary<string, string> { ["MODE"] = "test" } : new Dictionary<string, string>()
        };
        public void Dispose()
        {
            Db.Dispose();
            Environment.SetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID", previousApiInstanceId);
        }
    }

    private sealed class RecordingTransport : IHostContainerTransport
    {
        public string? ConnectionId { get; private set; }
        public string? Method { get; private set; }
        public HostContainerExecutionRequest? Request { get; private set; }
        public int Invocations { get; private set; }
        public HostContainerExecutionResponse Result { get; set; } = new();
        public bool Throw { get; set; }
        public Task<HostContainerExecutionResponse> ExecuteAsync(string connectionId, HostContainerExecutionRequest request, CancellationToken cancellationToken)
        {
            ConnectionId = connectionId;
            Method = AutomatonHubMethods.ExecuteHostContainer;
            Request = request;
            Invocations++;
            if (Throw) throw new InvalidOperationException("test transport failure");
            return Task.FromResult(Result);
        }
    }
}
