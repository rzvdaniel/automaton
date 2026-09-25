using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;
using System.Text.Json;

namespace Automaton.Cli.Tests.Services;

/// <summary>Builds a local application fixture and controllable deploy callbacks for focused service tests.</summary>
internal sealed class DeployServiceTestProject(string root)
{
    internal string Root { get; } = root;
    internal ConfigService Config { get; } = new();
    internal int SnapshotCalls { get; private set; }
    internal int DeployCalls { get; private set; }

    internal void Configure(
        string name = "App",
        bool runtimeCommand = true,
        bool buildCommand = true,
        bool includeBackend = true)
    {
        var application = ApplicationContractTestFactory.Create();
        if (!runtimeCommand) application.Runtime.Command = null;
        if (!buildCommand) application.Build.Command = null;
        Config.SaveProjectConfig(Root, new AutomatonConfig { Name = name, Application = application });
        SaveEnvironment();
        WriteFrontend();
        if (includeBackend) WriteBackend();
    }

    internal DeployService CreateService(
        DeployRuntimeResult? runtimeResult = null,
        DeployUploadResult? uploadResult = null,
        List<string>? progress = null,
        Action<Stream>? inspectPackage = null,
        string? readinessResponse = null)
        => new(
            Config,
            progress: progress == null ? null : new Action<string>(progress.Add),
            deployPackageAsync: (_, _, _, _, zip, _, _) =>
                DeployPackageAsync(zip, inspectPackage, uploadResult, runtimeResult),
            createSnapshotAsync: (_, _, _, _) => CreateSnapshot(),
            ensureManagedRuntimeReadyAsync: readinessResponse == null
                ? null
                : (_, _, _, _) => Task.FromResult(ParseResponse(readinessResponse)));

    private async Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> DeployPackageAsync(
        Stream package,
        Action<Stream>? inspectPackage,
        DeployUploadResult? uploadResult,
        DeployRuntimeResult? runtimeResult)
    {
        DeployCalls++;
        inspectPackage?.Invoke(package);
        if (runtimeResult == null)
        {
            throw new InvalidOperationException("No deployment result was configured for this test.");
        }
        await Task.CompletedTask;
        return (uploadResult ?? new DeployUploadResult(1, 0, 0.1), runtimeResult);
    }

    private Task<(bool Created, string Message)> CreateSnapshot()
    {
        SnapshotCalls++;
        return Task.FromResult((true, "snapshot created"));
    }

    private static JsonElement ParseResponse(string response)
    {
        using var document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    private void SaveEnvironment()
    {
        Config.SaveNamedEnvironmentConfig(Root, "local", new GlobalConfig
        {
            Server = "https://api.example.com",
            ApiKey = "ak_test",
            AppId = Guid.NewGuid().ToString()
        });
        Config.SaveProjectEnvironmentSelection(Root, new ProjectEnvironmentSelection { Default = "local" });
    }

    private void WriteFrontend()
    {
        var frontend = Path.Combine(Root, "frontend", "dist");
        Directory.CreateDirectory(frontend);
        File.WriteAllText(Path.Combine(frontend, "index.html"), "<html><body>ok</body></html>");
    }

    private void WriteBackend()
    {
        var backend = Path.Combine(Root, "backend");
        Directory.CreateDirectory(backend);
        File.WriteAllText(
            Path.Combine(backend, "todos.py"),
            "def handle(action_name, inputs):\n    return {'success': True}\n");
    }
}
