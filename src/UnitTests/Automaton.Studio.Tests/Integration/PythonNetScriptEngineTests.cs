using Automaton.Core.Scripting;
using FluentAssertions;
using NUnit.Framework;

namespace Automaton.Studio.Tests.Integration;

[TestFixture]
public class PythonNetScriptEngineTests
{
    [Test]
    public void PythonNetScriptEngine_WithSimplePythonCode_ExecutesSuccessfully()
    {
        // Arrange
        var engine = new PythonNetScriptEngine();
        
        var code = @"
result = 'Hello from Python.NET!'
number = 42
calculated = number * 2
is_boolean = True
";

        // Act
        var variables = engine.Execute(code, new Dictionary<string, object>());

        // Assert
        variables.Should().ContainKey("result");
        variables.Should().ContainKey("number");
        variables.Should().ContainKey("calculated");
        variables.Should().ContainKey("is_boolean");
        
        variables["result"].ToString().Should().Be("Hello from Python.NET!");
        variables["number"].Should().Be(42L);
        variables["calculated"].Should().Be(84L);
        variables["is_boolean"].Should().Be(true);
    }

    [Test]
    public void PythonNetScriptEngine_WithInputVariables_PassesCorrectly()
    {
        // Arrange
        var engine = new PythonNetScriptEngine();
        
        var code = @"
# Use input variables
greeting_message = f'{greeting} {name}'
result_length = len(greeting_message)
doubled_number = input_number * 2
";

        var inputs = new Dictionary<string, object>
        {
            ["greeting"] = "Hello",
            ["name"] = "World",
            ["input_number"] = 21
        };

        // Act
        var variables = engine.Execute(code, inputs);

        // Assert
        variables.Should().ContainKey("greeting_message");
        variables.Should().ContainKey("result_length");
        variables.Should().ContainKey("doubled_number");
        
        variables["greeting_message"].ToString().Should().Be("Hello World");
        variables["result_length"].Should().Be(11L);
        variables["doubled_number"].Should().Be(42L);
    }

    [Test]
    public void PythonNetScriptEngine_WithOutputCallback_CapturesOutput()
    {
        // Arrange
        var engine = new PythonNetScriptEngine();
        var capturedOutput = new List<string>();
        
        engine.OutputCallback = output => capturedOutput.Add(output);
        
        var code = @"
print('This is a test message')
print('This is another message')
result = 'Script completed'
";

        // Act
        var variables = engine.Execute(code, new Dictionary<string, object>());

        // Assert
        variables.Should().ContainKey("result");
        variables["result"].ToString().Should().Be("Script completed");
        
        capturedOutput.Should().NotBeEmpty();
        var allOutput = string.Join("", capturedOutput);
        allOutput.Should().Contain("This is a test message");
        allOutput.Should().Contain("This is another message");
    }

    [Test]
    public void PythonNetScriptEngine_WithStandardLibraryImport_WorksCorrectly()
    {
        // Arrange
        var engine = new PythonNetScriptEngine();
        
        var code = @"
import os
import sys
import json

# Test standard library functionality
current_dir = os.getcwd()
python_version = f'{sys.version_info.major}.{sys.version_info.minor}'

# Test JSON serialization
data = {'name': 'test', 'value': 123}
json_string = json.dumps(data)
parsed_data = json.loads(json_string)

result = f'Python {python_version} working correctly'
json_test_passed = parsed_data['value'] == 123
";

        // Act
        var variables = engine.Execute(code, new Dictionary<string, object>());

        // Assert
        variables.Should().ContainKey("result");
        variables.Should().ContainKey("json_test_passed");
        variables.Should().ContainKey("python_version");
        
        variables["json_test_passed"].Should().Be(true);
        variables["result"].ToString().Should().StartWith("Python");
    }

    [Test]
    [Ignore("This test requires the 'requests' module to be installed. Remove [Ignore] if requests is available.")]
    public void PythonNetScriptEngine_WithRequestsModule_CanImport()
    {
        // Arrange
        var engine = new PythonNetScriptEngine();
        
        var code = @"
try:
    import requests
    requests_available = True
    requests_version = requests.__version__
    result = f'Requests module version {requests_version} imported successfully'
except ImportError as e:
    requests_available = False
    result = f'Requests module not available: {str(e)}'
except Exception as e:
    requests_available = False
    result = f'Error importing requests: {str(e)}'
";

        // Act
        var variables = engine.Execute(code, new Dictionary<string, object>());

        // Assert
        variables.Should().ContainKey("result");
        variables.Should().ContainKey("requests_available");
        
        if (variables["requests_available"].Equals(true))
        {
            variables["result"].ToString().Should().Contain("imported successfully");
        }
        else
        {
            // If requests is not available, the test will show what went wrong
            TestContext.WriteLine($"Requests import result: {variables["result"]}");
        }
    }

    [Test]
    public void PythonNetScriptEngine_WithPythonError_ThrowsException()
    {
        // Arrange
        var engine = new PythonNetScriptEngine();
        
        var code = @"
# This will cause a Python error
undefined_variable = non_existent_var + 1
";

        // Act & Assert
        var exception = Assert.Throws<Exception>(() => engine.Execute(code, new Dictionary<string, object>()));
        exception.Message.Should().StartWith("Python Error:");
    }

    [Test]
    public void PythonNetScriptEngine_WithModernPythonSyntax_ExecutesCorrectly()
    {
        // Arrange
        var engine = new PythonNetScriptEngine();
        
        var code = @"
# Test modern Python features
name = 'World'
greeting = f'Hello, {name}!'  # f-strings (Python 3.6+)

# List comprehension
numbers = [x**2 for x in range(5)]

# Dictionary comprehension  
squares_dict = {x: x**2 for x in range(3)}

# Walrus operator (Python 3.8+) - might not be available in all Python versions
try:
    if (length := len(greeting)) > 5:
        walrus_result = f'Length is {length}'
    else:
        walrus_result = 'Short message'
    walrus_supported = True
except SyntaxError:
    walrus_result = 'Walrus operator not supported in this Python version'
    walrus_supported = False

result = 'Modern Python syntax test completed'
";

        // Act
        var variables = engine.Execute(code, new Dictionary<string, object>());

        // Assert
        variables.Should().ContainKey("greeting");
        variables.Should().ContainKey("numbers");
        variables.Should().ContainKey("squares_dict");
        variables.Should().ContainKey("result");
        
        variables["greeting"].ToString().Should().Be("Hello, World!");
        variables["result"].ToString().Should().Be("Modern Python syntax test completed");
        
        // Verify list comprehension worked
        var numbers = variables["numbers"] as List<object>;
        numbers.Should().NotBeNull();
        numbers.Should().HaveCount(5);
    }

    [Test]
    public void ScriptEngineFactory_WithPythonNetConfiguration_UsesPythonNet()
    {
        // Arrange
        var config = new ScriptEngineConfiguration { UsePythonNet = true };
        var factory = new ScriptEngineFactory(config);

        // Act
        var engine = factory.GetEngine("text/x-python");
        var engineType = factory.GetPythonEngineType();

        // Assert
        engine.Should().BeOfType<PythonNetScriptEngine>();
        engineType.Should().Be("Python.NET");
    }

    [Test]
    public void ScriptEngineFactory_WithDefaultConfiguration_UsesIronPython()
    {
        // Arrange
        var config = new ScriptEngineConfiguration(); // UsePythonNet defaults to false
        var factory = new ScriptEngineFactory(config);

        // Act
        var engine = factory.GetEngine("text/x-python");
        var engineType = factory.GetPythonEngineType();

        // Assert
        engine.Should().BeOfType<PythonScriptEngine>();
        engineType.Should().Be("IronPython");
    }
}