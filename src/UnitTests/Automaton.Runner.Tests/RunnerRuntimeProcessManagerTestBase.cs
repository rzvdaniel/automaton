using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[NonParallelizable]
public abstract class RunnerRuntimeProcessManagerTestBase
{    protected string root = null!;
    private string? originalRoot;
    private string? originalGatewayPort;
    private string? originalRuntimePortStart;
    private string? originalRuntimePortEnd;

    [SetUp]
    public void SetUp()
    {
        originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalGatewayPort = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT");
        originalRuntimePortStart = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START");
        originalRuntimePortEnd = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END");
        root = Path.Combine(Path.GetTempPath(), "automaton-runtime-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", originalGatewayPort);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START", originalRuntimePortStart);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END", originalRuntimePortEnd);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

}
