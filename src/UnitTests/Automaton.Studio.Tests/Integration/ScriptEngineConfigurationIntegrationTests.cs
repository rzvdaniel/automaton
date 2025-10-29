using Automaton.Core.Scripting;
using Automaton.Core.Scripting.Engines;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
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
        // Convert object dictionary to string dictionary
      var stringInputs = new Dictionary<string, string>();
        var variables = engine.Execute(code, stringInputs);

        // Assert
        variables.Should().ContainKey("result");
        variables.Should().ContainKey("number");
        variables.Should().ContainKey("calculated");
      
        // Deserialize JSON strings for comparison
        JsonConvert.DeserializeObject<string>(variables["result"]).Should().Be("Hello from Python!");
        JsonConvert.DeserializeObject<long>(variables["number"]).Should().Be(42L);
        JsonConvert.DeserializeObject<long>(variables["calculated"]).Should().Be(84L);
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
greeting_message = f'{greeting} {name}!
result_length = len(greeting_message)
";

        // Convert inputs to JSON strings
        var inputs = new Dictionary<string, string>
        {
            ["greeting"] = JsonConvert.SerializeObject("Hello"),
            ["name"] = JsonConvert.SerializeObject("World")
        };

 // Act
 var variables = engine.Execute(code, inputs);

     // Assert
        variables.Should().ContainKey("greeting_message");
   variables.Should().ContainKey("result_length");
        
   // Deserialize JSON strings for comparison
   JsonConvert.DeserializeObject<string>(variables["greeting_message"]).Should().Be("Hello World!");
        JsonConvert.DeserializeObject<long>(variables["result_length"]).Should().Be(12L);
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