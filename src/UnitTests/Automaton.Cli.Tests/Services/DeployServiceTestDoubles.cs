using System.Text.Json;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

internal static class DeployServiceTestDoubles
{
    public static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> SuccessfulPackage(
        string appId, string server, string apiKey, string? target, Stream package, bool clean, CancellationToken cancellationToken)
    {
        Assert.That(package.Length, Is.GreaterThan(0));
        return Task.FromResult((
            new DeployUploadResult(5, 0, 0.2),
            new DeployRuntimeResult(true, 1, "package deployed", Logs: ["stdout: runner build diagnostic"])));
    }

    public static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> ManagedRuntimePreparing(
        string appId, string server, string apiKey, string? target, Stream package, bool clean, CancellationToken cancellationToken)
        => PackageResult(new DeployRuntimeResult(
            false,
            0,
            "Managed runtime is being prepared.",
            "preparingManagedRuntime",
            true));

    public static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> ProtocolUpgradeRequired(
        string appId, string server, string apiKey, string? target, Stream package, bool clean, CancellationToken cancellationToken)
        => PackageResult(new DeployRuntimeResult(
            false,
            0,
            "Runner 'hosted' must be upgraded for container-based application execution before deployment.",
            "managedRuntimeUnavailable",
            false));

    public static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> InfrastructureRunnerMissing(
        string appId, string server, string apiKey, string? target, Stream package, bool clean, CancellationToken cancellationToken)
        => PackageResult(new DeployRuntimeResult(
            false,
            0,
            "Normal hosted Runner provisioning requires an Infrastructure Runner transport.",
            "managedRunnerProvisionFailed",
            false));

    public static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> AssignedRunnerOffline(
        string appId, string server, string apiKey, string? target, Stream package, bool clean, CancellationToken cancellationToken)
        => PackageResult(new DeployRuntimeResult(
            false,
            0,
            "No connected runner is assigned to this app.",
            "managedRuntimeUnavailable",
            false));

    public static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> SuccessfulDeployment(
        string appId, string server, string apiKey, string? target, Stream package, bool clean, CancellationToken cancellationToken)
        => PackageResult(new DeployRuntimeResult(true, 1, "deployed"));

    public static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> PlanCapabilityMissing(
        string appId, string server, string apiKey, string? target, Stream package, bool clean, CancellationToken cancellationToken)
        => PackageResult(new DeployRuntimeResult(
            false,
            0,
            "Your subscription plan does not include a hosted runner.",
            "managedRunnerProvisionFailed",
            false));

    public static Task<JsonElement> ManagedRunnerProvisionFailed(Action onRequest)
    {
        onRequest();
        using var document = JsonDocument.Parse(
            "{\"isReady\":false,\"state\":\"managedRunnerProvisionFailed\",\"message\":\"Managed runner could not be provisioned.\",\"requiresRetry\":true}");
        return Task.FromResult(document.RootElement.Clone());
    }

    public static async Task AssertFrontendOnlyReadinessFailureAsync(string projectRoot, ConfigService config)
    {
        var scenario = new FrontendReadinessScenario(config);
        var result = await scenario.RunAsync(projectRoot);
        scenario.AssertResult(result);
    }

    public static void AssertManagedRuntimePreparing(DeployResult result)
    {
        Assert.That(result.Success, Is.True);
        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.NextSteps, Does.Contain("Managed runtime is being prepared. Rerun automaton deploy in a moment."));
        Assert.That(result.NextSteps.Any(step => step.Contains("--runner", StringComparison.Ordinal)), Is.False);
        Assert.That(result.Checks.Last(check => check.Name == DeployCheckNames.PackageDeploy).Success, Is.False);
    }

    public static void AssertProtocolUpgradeGuidance(DeployResult result)
    {
        Assert.That(result.Success, Is.True);
        Assert.That(result.NextSteps, Does.Contain("The assigned Runner does not support the current container-based application deployment protocol."));
        Assert.That(result.NextSteps, Does.Contain("Ask an administrator to upgrade the Hosted Runner, or assign a compatible Runner, then rerun automaton deploy."));
        Assert.That(result.NextSteps.Any(step => step.Contains("--runner", StringComparison.Ordinal)), Is.False);
        Assert.That(result.NextSteps.Any(step => step.Contains("plan", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(result.Checks.Last(check => check.Name == DeployCheckNames.PackageDeploy).Message,
            Does.Contain("Runner 'hosted' must be upgraded for container-based application execution before deployment."));
    }

    public static void AssertInfrastructureRunnerGuidance(DeployResult result)
    {
        Assert.That(result.NextSteps, Does.Contain("No eligible Runner Host is available for managed Hosted Runner provisioning."));
        Assert.That(result.NextSteps.Any(step => step.Contains("protocol-4 Infrastructure Runner with HostContainerExecution", StringComparison.Ordinal)), Is.True);
        Assert.That(result.NextSteps.Any(step => step.Contains("plan does not include", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    public static void AssertAssignedRunnerOfflineGuidance(DeployResult result)
    {
        Assert.That(result.Success, Is.True);
        Assert.That(result.NextSteps, Does.Contain("This app is assigned to a runner, but that runner is not currently connected."));
        Assert.That(result.NextSteps, Does.Contain("Bring the assigned runner online or reassign the app to a healthy runner in Automaton Studio, then rerun automaton deploy."));
        Assert.That(result.NextSteps.Any(step => step.Contains("plan does not include", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(result.Checks.Last(check => check.Name == DeployCheckNames.PackageDeploy).Message,
            Does.Contain("No connected runner is assigned to this app."));
    }

    private sealed class FrontendReadinessScenario(ConfigService config)
    {
        private bool readinessCalled;

        public async Task<DeployResult> RunAsync(string projectRoot)
        {
            var service = new DeployService(
                config,
                checkReported: null,
                deployPackageAsync: (_, _, _, _, _, _, _) => throw new InvalidOperationException("upload must wait for runtime readiness"),
                createSnapshotAsync: (_, _, _, _) => Task.FromResult((true, "snapshot created")),
                ensureManagedRuntimeReadyAsync: (_, _, _, _) => ManagedRunnerProvisionFailed(() => readinessCalled = true));
            return await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);
        }

        public void AssertResult(DeployResult result)
        {
            Assert.That(readinessCalled, Is.True);
            Assert.That(result.Success, Is.False);
            Assert.That(result.NextSteps, Does.Contain("Managed runner could not be provisioned."));
            Assert.That(result.Checks.Single(check => check.Name == DeployCheckNames.RunnerAssignment).IsWarning, Is.False);
        }
    }

    private static Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> PackageResult(DeployRuntimeResult result)
        => Task.FromResult((new DeployUploadResult(1, 0, 0.1), result));
}
