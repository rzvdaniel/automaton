using Automaton.Core.Scripting;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Studio.Tests.Integration;

[TestFixture]
public class ScriptEngineConfigurationIntegrationTests
{
    [Test]
    public void ScriptEngineFactory_WithConfiguration_ConfiguresCorrectly()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["Scripting:PythonPaths:0"] = Path.GetTempPath(),
            ["Scripting:EnableDebugMode"] = "true",
            ["Scripting:AutoDetectPythonPaths"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScripting();

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var scriptEngineConfig = serviceProvider.GetService<ScriptEngineConfiguration>();
        var scriptEngineFactory = serviceProvider.GetService<ScriptEngineFactory>();

        // Assert
        scriptEngineConfig.Should().NotBeNull();
        scriptEngineConfig.AutoDetectPythonPaths.Should().BeFalse();
        scriptEngineConfig.PythonPaths.Should().Contain(Path.GetTempPath());

        scriptEngineFactory.Should().NotBeNull();
    }

    [Test]
    public void ScriptEngineFactory_WithPythonCode_ExecutesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScripting();
        var serviceProvider = services.BuildServiceProvider();

        var scriptEngineFactory = serviceProvider.GetService<ScriptEngineFactory>();
        var engine = scriptEngineFactory!.GetEngine("text/x-python");

        var code = @"
result = 'Hello from Python!'
number = 42
calculated = number * 2
";

        // Act
        var variables = engine.Execute(code, new Dictionary<string, object>());

        // Assert
        variables.Should().ContainKey("result");
        variables.Should().ContainKey("number");
        variables.Should().ContainKey("calculated");
        
        variables["result"].ToString().Should().Be("Hello from Python!");
        variables["number"].ToString().Should().Be("42");
        variables["calculated"].ToString().Should().Be("84");
    }

    [Test]
    public void ScriptEngineFactory_WithInputVariables_PassesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScripting();
        var serviceProvider = services.BuildServiceProvider();

        var scriptEngineFactory = serviceProvider.GetService<ScriptEngineFactory>();
        var engine = scriptEngineFactory!.GetEngine("text/x-python");

        var code = @"
# Use input variables
greeting_message = f'{greeting} {name}!'
result_length = len(greeting_message)
";

        var inputs = new Dictionary<string, object>
        {
            ["greeting"] = "Hello",
            ["name"] = "World"
        };

        // Act
        var variables = engine.Execute(code, inputs);

        // Assert
        variables.Should().ContainKey("greeting_message");
        variables.Should().ContainKey("result_length");
        
        variables["greeting_message"].ToString().Should().Be("Hello World!");
        variables["result_length"].ToString().Should().Be("12");
    }

    [Test]
    public void ScriptEngineConfiguration_WithManualConfiguration_OverridesDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScripting(config =>
        {
            config.AutoDetectPythonPaths = false;
            config.AddPythonPath(Path.GetTempPath());
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var config = serviceProvider.GetService<ScriptEngineConfiguration>();

        // Assert
        config.Should().NotBeNull();
        config!.AutoDetectPythonPaths.Should().BeFalse();
        config.PythonPaths.Should().Contain(Path.GetTempPath());
    }
}