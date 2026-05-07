using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class LocalAppExecutionServiceTests
{
    private readonly List<string> _tempDirectories = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _tempDirectories)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        _tempDirectories.Clear();
    }

    [Test]
    public async Task ExecuteAsync_WhenMockOnly_ReturnsMockResponse()
    {
        var projectRoot = CreateTempDirectory();
        var context = CreateContext(projectRoot);
        Directory.CreateDirectory(context.MocksPath);
        await File.WriteAllTextAsync(Path.Combine(context.MocksPath, "todos.list.json"), "{\"success\":true,\"data\":{\"items\":[1,2]},\"error\":\"\"}");

        var service = new LocalAppExecutionService(new PythonRuntimeService(new ConfigService()));

        var result = await service.ExecuteAsync(context, new LocalAppExecutionOptions
        {
            ControllerName = "todos",
            ActionName = "list",
            InputsJson = "{}",
            Mode = LocalAppExecutionModes.MockOnly,
            Timeout = TimeSpan.FromSeconds(5),
            Verbose = false
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data, Is.TypeOf<JsonElement>());
        var data = (JsonElement)result.Data!;
        Assert.That(data.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task ExecuteAsync_WhenControllerExists_ExecutesPythonHandle()
    {
        var projectRoot = CreateTempDirectory();
        var context = CreateContext(projectRoot);
        Directory.CreateDirectory(context.BackendPath);
        await File.WriteAllTextAsync(Path.Combine(context.BackendPath, "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {'action': action_name, 'value': inputs.get('value')}, 'error': ''}\n");

        var pythonRuntimeService = new PythonRuntimeService(new ConfigService());
        try
        {
            await pythonRuntimeService.ResolveAsync(projectRoot);
        }
        catch
        {
            Assert.Ignore("Python runtime not available in the test environment.");
        }

        var service = new LocalAppExecutionService(pythonRuntimeService);

        var result = await service.ExecuteAsync(context, new LocalAppExecutionOptions
        {
            ControllerName = "todos",
            ActionName = "echo",
            InputsJson = "{\"value\":42}",
            Mode = LocalAppExecutionModes.Native,
            Timeout = TimeSpan.FromSeconds(10),
            Verbose = false
        });

        Assert.That(result.Success, Is.True);
        var data = (JsonElement)result.Data!;
        Assert.That(data.GetProperty("action").GetString(), Is.EqualTo("echo"));
        Assert.That(data.GetProperty("value").GetInt32(), Is.EqualTo(42));
    }

    private LocalAppContext CreateContext(string projectRoot)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend"));
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "index.html"), "<html></html>");
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend", "mocks"));

        return new LocalAppContext
        {
            RootPath = projectRoot,
            FrontendPath = Path.Combine(projectRoot, "frontend"),
            IndexHtmlPath = Path.Combine(projectRoot, "frontend", "index.html"),
            BackendPath = Path.Combine(projectRoot, "backend"),
            MocksPath = Path.Combine(projectRoot, "backend", "mocks"),
            DataPath = Path.Combine(projectRoot, "data"),
            DisplayName = "Test App"
        };
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
