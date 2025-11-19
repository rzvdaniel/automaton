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