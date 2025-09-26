using Automaton.Studio.Services;
using FluentAssertions;
using NUnit.Framework;

namespace Automaton.Studio.Tests.Services;

[TestFixture]
public class AiCodeCleanupServiceTests
{
    private AiCodeCleanupService _codeCleanupService;

    [SetUp]
    public void SetUp()
    {
        _codeCleanupService = new AiCodeCleanupService();
    }

    [Test]
    public void CleanupGeneratedCode_WithNullInput_ReturnsEmptyString()
    {
        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void CleanupGeneratedCode_WithEmptyInput_ReturnsEmptyString()
    {
        // Act
        var result = _codeCleanupService.CleanupGeneratedCode("");

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void CleanupGeneratedCode_WithWhitespaceOnly_ReturnsEmptyString()
    {
        // Act
        var result = _codeCleanupService.CleanupGeneratedCode("   \n\t  ");

        // Assert
        result.Should().BeEmpty();
    }

    [TestCase("python")]
    [TestCase("Python")]
    [TestCase("PYTHON")]
    [TestCase("batch")]
    [TestCase("Batch")]
    [TestCase("BATCH")]
    [TestCase("csharp")]
    [TestCase("javascript")]
    public void CleanupGeneratedCode_WithLanguageSpecificCodeBlock_ExtractsCodeContent(string language)
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"```{language}\n{codeContent}\n```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithCodeBlockWithoutLanguage_ExtractsCodeContent()
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"```\n{codeContent}\n```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithCodeBlockWithoutNewlines_ExtractsCodeContent()
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"```python{codeContent}```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithMultilineCode_PreservesLineBreaks()
    {
        // Arrange
        var codeContent = "import os\nprint('Hello, World!')\nprint('Second line')";
        var input = $"```python\n{codeContent}\n```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithSimpleCodeBlockPattern_ExtractsCodeContent()
    {
        // Arrange
        var codeContent = "echo 'Hello World'";
        var input = $"```{codeContent}```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithExtraWhitespaceAroundCodeBlock_TrimsWhitespace()
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"  ```python\n  {codeContent}  \n```  ";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithCodeBlockInMiddleOfText_ExtractsOnlyCodeContent()
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"Here is some code:\n```python\n{codeContent}\n```\nThat was the code.";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithMultipleCodeBlocks_ExtractsFirstCodeBlock()
    {
        // Arrange
        var firstCodeContent = "print('First block')";
        var secondCodeContent = "print('Second block')";
        var input = $"```python\n{firstCodeContent}\n```\nSome text\n```python\n{secondCodeContent}\n```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(firstCodeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithNoCodeBlock_ReturnsOriginalTrimmed()
    {
        // Arrange
        var input = "  This is just regular text without code blocks  ";
        var expected = "This is just regular text without code blocks";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    public void CleanupGeneratedCode_WithIncompleteCodeBlock_ReturnsOriginalTrimmed()
    {
        // Arrange
        var input = "```python\nprint('Hello, World!')";
        var expected = "```python\nprint('Hello, World!')";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    public void CleanupGeneratedCode_WithBackticksInContent_HandlesCorrectly()
    {
        // Arrange
        var codeContent = "echo `date`";
        var input = $"```bash\n{codeContent}\n```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithRealWorldPythonExample_ExtractsCorrectly()
    {
        // Arrange
        var codeContent = @"import requests
import json

def fetch_data(url):
    response = requests.get(url)
    if response.status_code == 200:
        return response.json()
    return None

data = fetch_data('https://api.example.com/data')
print(json.dumps(data, indent=2))";
        var input = $"Here's a Python script that fetches data from an API:\n\n```python\n{codeContent}\n```\n\nThis script will help you get started.";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [Test]
    public void CleanupGeneratedCode_WithRealWorldBatchExample_ExtractsCorrectly()
    {
        // Arrange
        var codeContent = @"@echo off
set /p input=""Enter your name: ""
echo Hello, %input%!
pause";
        var input = $"Here's a batch script:\n\n```batch\n{codeContent}\n```\n\nThis will greet the user.";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
    }

    [TestCase("```\ncode\n```")]
    [TestCase("``` \ncode\n```")]
    [TestCase("```\n\ncode\n\n```")]
    [TestCase("``` python \ncode\n ```")]
    public void CleanupGeneratedCode_WithVariousWhitespacePatterns_HandlesCorrectly(string input)
    {
        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be("code");
    }

    [Test]
    public void CleanupGeneratedCode_WithEmptyCodeBlock_ReturnsEmptyString()
    {
        // Arrange
        var input = "```python\n\n```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void CleanupGeneratedCode_WithOnlyWhitespaceInCodeBlock_ReturnsEmptyString()
    {
        // Arrange
        var input = "```python\n   \n\t\n  \n```";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void CleanupGeneratedCode_WithRequestsImportExample_ExtractsCorrectly()
    {
        // Arrange
        var codeContent = @"import requests

# Example input variables
country = ""US""
city = ""New York""

# Fetch the weather for the given city and country
weather = fetch_weather(city, country)

# Output variable
print(f""Weather in {city}, {country}: {weather}"")";
        var input = $"Here's the Python code with requests:\n\n```python\n{codeContent}\n```\n\nThis demonstrates the issue.";

        // Act
        var result = _codeCleanupService.CleanupGeneratedCode(input);

        // Assert
        result.Should().Be(codeContent);
        result.Should().Contain("import requests");
    }
}