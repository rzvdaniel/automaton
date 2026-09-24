using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;

namespace Automaton.Cli.Tests.Services;

internal static class DeployServiceTargetScenario
{
    public static async Task RunAsync()
    {
        var root = CreateProject();
        try
        {
            var targetSlug = string.Empty;
            var target = CreateTarget();
            var service = CreateService(CreateConfiguration(root), slug => targetSlug = slug);
            var result = await service.DeployAsync(root, clean: false, directoryOverride: null, target: target);
            AssertDeployment(result, targetSlug);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var frontendDist = Path.Combine(root, "frontend", "dist");
        Directory.CreateDirectory(frontendDist);
        File.WriteAllText(Path.Combine(frontendDist, "index.html"), "<html><body>ok</body></html>");
        return root;
    }

    private static ConfigService CreateConfiguration(string root)
    {
        var config = new ConfigService();
        config.SaveProjectConfig(root, new AutomatonConfig
        {
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        config.SaveNamedEnvironmentConfig(root, "local", new GlobalConfig
        {
            Server = "https://api.example.com",
            ApiKey = "ak_test",
            AppId = Guid.NewGuid().ToString()
        });
        config.SaveProjectEnvironmentSelection(root, new ProjectEnvironmentSelection { Default = "local" });
        return config;
    }

    private static DeploymentTargetInfo CreateTarget()
        => new(
            "target-eu",
            "Server EU",
            "server-eu",
            false,
            "Running",
            null,
            new DeploymentTargetRunnerInfo("runner-eu", "Runner EU", true));

    private static DeployService CreateService(ConfigService config, Action<string?> recordTarget)
    {
        return new DeployService(
            config,
            checkReported: null,
            deployPackageAsync: (_, _, _, targetSlug, _, _, _) =>
            {
                recordTarget(targetSlug);
                return Task.FromResult((
                    new DeployUploadResult(1, 0, 0.1),
                    new DeployRuntimeResult(true, 1, "deployed")));
            },
            createSnapshotAsync: (_, _, _, _) => Task.FromResult((true, "snapshot created")));
    }

    private static void AssertDeployment(DeployResult result, string targetSlug)
    {
        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(targetSlug, NUnit.Framework.Is.EqualTo("server-eu"));
            NUnit.Framework.Assert.That(result.TargetSlug, NUnit.Framework.Is.EqualTo("server-eu"));
            NUnit.Framework.Assert.That(result.RunnerName, NUnit.Framework.Is.EqualTo("Runner EU"));
            NUnit.Framework.Assert.That(result.DeploymentMode, NUnit.Framework.Is.EqualTo("named"));
        });
    }
}
