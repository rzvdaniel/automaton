using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class AppHomeCardRunnerTests
{
    [Test]
    public void HomeCardActionResultIsTheWholeCard()
        => Assert.That(AppActionProcessExecutor.ExtractHomeCard("home-card", """{"status":"ok"}"""), Is.EqualTo("""{"status":"ok"}"""));

    [Test]
    public void OtherActionsOfferTheirHomeCardProperty()
    {
        Assert.That(AppActionProcessExecutor.ExtractHomeCard("backup", """{"done":true,"homeCard":{"status":"ok"}}"""), Is.EqualTo("""{"status":"ok"}"""));
        Assert.That(AppActionProcessExecutor.ExtractHomeCard("backup", """{"done":true}"""), Is.Null);
        Assert.That(AppActionProcessExecutor.ExtractHomeCard("backup", "[1]"), Is.Null);
        Assert.That(AppActionProcessExecutor.ExtractHomeCard("backup", "not json"), Is.Null);
    }

    [Test]
    public void ACardOverTheSizeLimitIsMarkedSoItNeverValidates()
        => Assert.That(AppActionProcessExecutor.ExtractHomeCard("home-card", $$"""{"status":"{{new string('a', 9000)}}"}"""), Is.EqualTo("!too-large"));

    [Test]
    public void ActionsGetTheirOwnAppsAddressOnlyWhileItRuns()
    {
        var original = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HOST_ADDRESS");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HOST_ADDRESS", "10.1.2.3");
        try
        {
            var registry = new RunnerRuntimeRegistry();
            Guid app = Guid.NewGuid(), target = Guid.NewGuid();
            var service = new AppActionExecutionService(new RunnerWorkspaceService(), null, null, registry);
            ExecuteAppActionModuleRequest Request(Guid appId) => new()
            {
                AppId = appId, DeploymentTargetId = target,
                Secrets = { ["AUTOMATON_APP_INTERNAL_URL"] = "http://evil.example" }, PlainValueNames = ["AUTOMATON_APP_INTERNAL_URL"]
            };

            var stopped = Request(app);
            service.AddAppInternalUrl(stopped, machineManagement: true);
            Assert.That(stopped.Secrets, Does.Not.ContainKey("AUTOMATON_APP_INTERNAL_URL"), "not running: no address, and an app secret cannot fake it");

            registry.Register(app, target, 32768);
            var running = Request(app);
            service.AddAppInternalUrl(running, machineManagement: true);
            Assert.That(running.Secrets["AUTOMATON_APP_INTERNAL_URL"], Is.EqualTo("http://10.1.2.3:32768"));
            Assert.That(running.PlainValueNames, Does.Contain("AUTOMATON_APP_INTERNAL_URL"), "plain text, not redacted");

            var other = Request(Guid.NewGuid());
            service.AddAppInternalUrl(other, machineManagement: true);
            Assert.That(other.Secrets, Does.Not.ContainKey("AUTOMATON_APP_INTERNAL_URL"), "another app's Actions get nothing");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HOST_ADDRESS", original);
        }
    }

    [Test]
    public void AContainerIsNotGivenALoopbackAddress()
    {
        var original = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HOST_ADDRESS");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HOST_ADDRESS", "127.0.0.1");
        try
        {
            var registry = new RunnerRuntimeRegistry();
            Guid app = Guid.NewGuid(), target = Guid.NewGuid();
            registry.Register(app, target, 32768);
            var service = new AppActionExecutionService(new RunnerWorkspaceService(), null, null, registry);
            var container = new ExecuteAppActionModuleRequest { AppId = app, DeploymentTargetId = target };
            var process = new ExecuteAppActionModuleRequest { AppId = app, DeploymentTargetId = target };

            service.AddAppInternalUrl(container, machineManagement: true);
            service.AddAppInternalUrl(process, machineManagement: false);

            Assert.That(container.Secrets, Does.Not.ContainKey("AUTOMATON_APP_INTERNAL_URL"));
            Assert.That(process.Secrets["AUTOMATON_APP_INTERNAL_URL"], Is.EqualTo("http://127.0.0.1:32768"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HOST_ADDRESS", original);
        }
    }
}
