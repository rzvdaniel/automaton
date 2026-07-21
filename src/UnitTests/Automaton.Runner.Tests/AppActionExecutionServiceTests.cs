using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class AppActionExecutionServiceTests
{
    [Test]
    public void CleanupExpiredArtifacts_RemovesOnlyExpiredActionExecutions()
    {
        var original = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-cleanup", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            var service = new AppActionExecutionService(workspace);
            var appId = Guid.NewGuid();
            var expired = Path.Combine(workspace.GetAppActionExecutionsDirectory(appId, "hello"), Guid.NewGuid().ToString("N"));
            var current = Path.Combine(workspace.GetAppActionExecutionsDirectory(appId, "hello"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(expired);
            Directory.CreateDirectory(current);
            Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-2));

            var removed = service.CleanupExpiredArtifacts();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(Directory.Exists(expired), Is.False);
            Assert.That(Directory.Exists(current), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", original);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
