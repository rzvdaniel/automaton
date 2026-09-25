using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.GenericRuntimeAcceptanceSupport;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class GenericRuntimeAcceptanceTests
{
    private string workspaceRoot = null!;
    private string? originalWorkspaceRoot;
    private string? originalScriptsRoot;
    private string? originalPortStart;
    private string? originalPortEnd;

    [SetUp]
    public void SetUp()
    {
        originalWorkspaceRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalScriptsRoot = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        originalPortStart = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START");
        originalPortEnd = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END");
        workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "a7",
            Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", null);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START", "24000");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END", "24999");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalWorkspaceRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", originalScriptsRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START", originalPortStart);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END", originalPortEnd);
        if (Directory.Exists(workspaceRoot))
            Directory.Delete(workspaceRoot, recursive: true);
    }

    [Test]
    public async Task DifferentStacks_UseOneGenericBuildRuntimeReadinessAndGatewayPath()
    {
        await GenericRuntimeAcceptanceScenario.RunAsync(workspaceRoot);
    }

    [Test]
    public async Task NodeReplacement_LeavesV1CurrentUntilV2ReadinessThenRoutesV2()
    {
        var fixture = AcceptanceFixture.Node;
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var processManager = CreateProcessManager(workspace, registry);
        var deployment = CreateDeployment(workspace, processManager);
        await using var gateway = await StartGatewayAsync(registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var firstPackage = CreatePackage(fixture, appId, "v1", 0);
        var replacementPackage = CreatePackage(fixture, appId, "v2", 250);
        Task<DeployAppPackageResult>? replacementTask = null;

        try
        {
            var firstResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = firstPackage,
                Clean = true
            });
            Assert.That(firstResult.Success, Is.True, firstResult.Message);

            var metadataPath = workspace.GetAppAutomationRoot(appId, targetId);
            var originalMetadata = await ReadMetadataAsync(workspace, appId, targetId);
            replacementTask = deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = replacementPackage,
                Clean = true
            });

            await WaitForCandidateAsync(metadataPath);
            var duringReplacement = await gateway.GetTextAsync(appId, targetId, "/hello");
            var replacementResult = await replacementTask;
            var afterReplacement = await gateway.GetTextAsync(appId, targetId, "/hello");
            var finalMetadata = await ReadMetadataAsync(workspace, appId, targetId);

            Assert.Multiple(() =>
            {
                Assert.That(duringReplacement, Does.Contain("node:v1:/hello"));
                Assert.That(replacementResult.Success, Is.True, replacementResult.Message);
                Assert.That(afterReplacement, Does.Contain("node:v2:/hello"));
                Assert.That(finalMetadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
                Assert.That(finalMetadata.Candidate, Is.Null);
                Assert.That(finalMetadata.ArtifactRoot, Is.Not.EqualTo(originalMetadata.ArtifactRoot));
            });
        }
        finally
        {
            if (replacementTask is not null && !replacementTask.IsCompleted)
                await replacementTask;
            await processManager.StopAsync(appId, targetId);
            DeleteFileIfExists(firstPackage);
            DeleteFileIfExists(replacementPackage);
        }
    }

    [Test]
    public async Task NodeFailedReplacement_PreservesHealthyV1RuntimeAndRoute()
    {
        var fixture = AcceptanceFixture.Node;
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var processManager = CreateProcessManager(workspace, registry);
        var deployment = CreateDeployment(workspace, processManager);
        await using var gateway = await StartGatewayAsync(registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var firstPackage = CreatePackage(fixture, appId, "v1", 0);
        var brokenPackage = CreatePackage(fixture, appId, "broken", 0, brokenRuntime: true);

        try
        {
            var firstResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = firstPackage,
                Clean = true
            });
            Assert.That(firstResult.Success, Is.True, firstResult.Message);

            var originalMetadata = await ReadMetadataAsync(workspace, appId, targetId);
            var failedResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = brokenPackage,
                Clean = true
            });
            var restoredMetadata = await ReadMetadataAsync(workspace, appId, targetId);
            var body = await gateway.GetTextAsync(appId, targetId, "/hello?after=failed");

            Assert.Multiple(() =>
            {
                Assert.That(failedResult.Success, Is.False);
                Assert.That(body, Does.Contain("node:v1:/hello?after=failed"));
                Assert.That(restoredMetadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
                Assert.That(restoredMetadata.ProcessId, Is.EqualTo(originalMetadata.ProcessId));
                Assert.That(restoredMetadata.ArtifactRoot, Is.EqualTo(originalMetadata.ArtifactRoot));
                Assert.That(restoredMetadata.Candidate, Is.Null);
            });
        }
        finally
        {
            await processManager.StopAsync(appId, targetId);
            DeleteFileIfExists(firstPackage);
            DeleteFileIfExists(brokenPackage);
        }
    }

}
