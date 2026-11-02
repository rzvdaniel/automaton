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

}
