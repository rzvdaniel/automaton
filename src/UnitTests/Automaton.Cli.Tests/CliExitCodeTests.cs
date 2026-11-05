using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests;

[TestFixture]
[NonParallelizable]
public sealed class CliExitCodeTests
{
    private string _directory = null!;
    private string? _originalConfigDir;
    private string _temp = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.GetCurrentDirectory();
        _originalConfigDir = Environment.GetEnvironmentVariable("AUTOMATON_CONFIG_DIR");
        _temp = Path.Combine(Path.GetTempPath(), "automaton-exit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_temp, "config"));
        Directory.CreateDirectory(Path.Combine(_temp, "app"));
        Environment.SetEnvironmentVariable("AUTOMATON_CONFIG_DIR", Path.Combine(_temp, "config"));
        Directory.SetCurrentDirectory(Path.Combine(_temp, "app"));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.SetCurrentDirectory(_directory);
        Environment.SetEnvironmentVariable("AUTOMATON_CONFIG_DIR", _originalConfigDir);
        Environment.ExitCode = 0;
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }

    // Secrets commands report failures through Environment.ExitCode, which the invocation result used to override.
    [TestCase("secrets", "list", "--json")]
    [TestCase("secrets", "delete", "K", "--project", "--json")]
    [TestCase("project", "add-app", "--json")]
    public async Task FailingCommandsExitNonZero(params string[] args)
    {
        var originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try { Assert.That(await CliApplication.RunAsync(args, new ConfigService()), Is.Not.Zero); }
        finally { Console.SetOut(originalOut); }
    }
}
