using Automaton.Core.Scripting;
using Automaton.Core.Scripting.Engines.Python;
using FluentAssertions;
using Newtonsoft.Json;
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
        var variables = engine.Execute(code, new Dictionary<string, string>());

        // Assert
        variables.Should().ContainKey("result");
        variables.Should().ContainKey("number");
        variables.Should().ContainKey("calculated");
        variables.Should().ContainKey("is_boolean");

        // ✅ Deserialize JSON strings for comparison
        JsonConvert.DeserializeObject<string>(variables["result"]).Should().Be("Hello from Python.NET!");
        JsonConvert.DeserializeObject<long>(variables["number"]).Should().Be(42L);
        JsonConvert.DeserializeObject<long>(variables["calculated"]).Should().Be(84L);
        JsonConvert.DeserializeObject<bool>(variables["is_boolean"]).Should().Be(true);
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
        var variables = engine.Execute(code, new Dictionary<string, string>());

        // Assert
        variables.Should().ContainKey("result");
        // ✅ Deserialize JSON string for comparison
        JsonConvert.DeserializeObject<string>(variables["result"]).Should().Be("Script completed");

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
        var variables = engine.Execute(code, new Dictionary<string, string>());

        // Assert
        variables.Should().ContainKey("result");
        variables.Should().ContainKey("json_test_passed");
        variables.Should().ContainKey("python_version");

        // ✅ Deserialize JSON strings for comparison
        JsonConvert.DeserializeObject<bool>(variables["json_test_passed"]).Should().Be(true);
        var resultStr = JsonConvert.DeserializeObject<string>(variables["result"]);
        resultStr.Should().StartWith("Python");
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
        var exception = Assert.Throws<Exception>(() => engine.Execute(code, new Dictionary<string, string>()));
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
        var variables = engine.Execute(code, new Dictionary<string, string>());

        // Assert
        variables.Should().ContainKey("greeting");
        variables.Should().ContainKey("numbers");
        variables.Should().ContainKey("squares_dict");
        variables.Should().ContainKey("result");

        // ✅ Deserialize JSON strings for comparison
        var greeting = JsonConvert.DeserializeObject<string>(variables["greeting"]);
        greeting.Should().Be("Hello, World!");

        var resultStr = JsonConvert.DeserializeObject<string>(variables["result"]);
        resultStr.Should().Be("Modern Python syntax test completed");

        // ✅ Deserialize list from JSON string
        var numbers = JsonConvert.DeserializeObject<List<long>>(variables["numbers"]);
        numbers.Should().NotBeNull();
        numbers.Should().HaveCount(5);
        numbers.Should().Equal(new[] { 0L, 1L, 4L, 9L, 16L });
    }
}