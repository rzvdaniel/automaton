using Automaton.Studio.Services;
using FluentAssertions;
using NUnit.Framework;

namespace Automaton.Studio.Tests.Services;

[TestFixture]
public class AiMessageFormattingServiceTests
{
    private IAiMessageFormattingService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new AiMessageFormattingService();
    }

    [Test]
    public void FormatReasoningTextWithCode_WithNullInput_ReturnsEmptyString()
    {
        // Act
        var result = _service.FormatReasoningTextWithCode(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void FormatReasoningTextWithCode_WithEmptyInput_ReturnsEmptyString()
    {
        // Act
        var result = _service.FormatReasoningTextWithCode("");

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void FormatReasoningTextWithCode_WithWhitespaceOnly_ReturnsHtmlEncodedWhitespace()
    {
        // Arrange
        var input = "   \n\t  ";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Be(System.Net.WebUtility.HtmlEncode(input));
    }

    [Test]
    public void FormatReasoningTextWithCode_WithNoCodeBlock_ReturnsHtmlEncodedText()
    {
        // Arrange
        var input = "This is just plain text without any code blocks.";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Be(System.Net.WebUtility.HtmlEncode(input));
    }

    [Test]
    public void FormatReasoningTextWithCode_WithHtmlCharacters_EncodesThemProperly()
    {
        // Arrange
        var input = "This has <html> & special characters";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("&lt;html&gt;");
        result.Should().Contain("&amp;");
    }

    [TestCase("python")]
    [TestCase("Python")]
    [TestCase("PYTHON")]
    [TestCase("csharp")]
    [TestCase("javascript")]
    [TestCase("bash")]
    public void FormatReasoningTextWithCode_WithLanguageSpecificCodeBlock_FormatsCorrectly(string language)
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"```{language}\n{codeContent}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain("<code>");
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
        result.Should().Contain("</code></pre>");
    }

    [Test]
    public void FormatReasoningTextWithCode_WithCodeBlockWithoutLanguage_FormatsCorrectly()
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"```\n{codeContent}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
    }

    [Test]
    public void FormatReasoningTextWithCode_WithMultilineCode_PreservesLineBreaks()
    {
        // Arrange
        var codeContent = "import os\nprint('Hello, World!')\nprint('Second line')";
        var input = $"```python\n{codeContent}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
        var decodedCode = System.Net.WebUtility.HtmlDecode(result);
        decodedCode.Should().Contain("\n");
    }

    [Test]
    public void FormatReasoningTextWithCode_WithCodeBlockInMiddleOfText_FormatsOnlyCodeBlock()
    {
        // Arrange
        var textBefore = "Here is some code:";
        var codeContent = "print('Hello, World!')";
        var textAfter = "That was the code.";
        var input = $"{textBefore}\n```python\n{codeContent}\n```\n{textAfter}";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(textBefore));
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(textAfter));
    }

    [Test]
    public void FormatReasoningTextWithCode_WithMultipleCodeBlocks_FormatsOnlyFirstCodeBlock()
    {
        // Arrange
        var firstCode = "print('First block')";
        var secondCode = "print('Second block')";
        var input = $"```python\n{firstCode}\n```\nSome text\n```python\n{secondCode}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(firstCode));
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(secondCode));
        // Both should be formatted
        var preCount = result.Split("<pre class='generated-code-block'>").Length - 1;
        preCount.Should().Be(2);
    }

    [Test]
    public void FormatReasoningTextWithCode_WithSpecialCharactersInCode_EncodesThemProperly()
    {
        // Arrange
        var codeContent = "if (x < 5 && y > 3) { return true; }";
        var input = $"```javascript\n{codeContent}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("&lt;");
        result.Should().Contain("&gt;");
        result.Should().Contain("&amp;&amp;");
    }

    [Test]
    public void FormatReasoningTextWithCode_WithRealWorldPythonExample_FormatsCorrectly()
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
        var textBefore = "Here's a Python script that fetches data from an API:";
        var textAfter = "This script will help you get started.";
        var input = $"{textBefore}\n\n```python\n{codeContent}\n```\n\n{textAfter}";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(textBefore));
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(textAfter));
    }

    [Test]
    public void FormatReasoningTextWithCode_WithBackticksInCodeContent_HandlesCorrectly()
    {
        // Arrange
        var codeContent = "echo `date`";
        var input = $"```bash\n{codeContent}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
    }

    [Test]
    public void FormatReasoningTextWithCode_WithEmptyCodeBlock_ReturnsPreWithoutCode()
    {
        // Arrange
        var input = "```python\n\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain("</pre>");
    }

    [Test]
    public void FormatReasoningTextWithCode_WithOnlyWhitespaceInCodeBlock_EncodesWhitespace()
    {
        // Arrange
        var input = "```python\n   \n\t\n  \n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("<pre class='generated-code-block'>");
    }

    [Test]
    public void FormatReasoningTextWithCode_WithExtraWhitespaceAroundCodeBlock_HandlesCorrectly()
    {
        // Arrange
        var codeContent = "print('Hello, World!')";
        var input = $"  ```python\n  {codeContent}  \n```  ";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
    }

    [Test]
    public void FormatReasoningTextWithCode_WithTextBeforeAndAfterMultipleBlocks_FormatsAll()
    {
        // Arrange
        var text1 = "First explanation";
        var code1 = "def func1():\n    pass";
        var text2 = "Second explanation";
        var code2 = "def func2():\n    pass";
        var text3 = "Final notes";
        var input = $"{text1}\n```python\n{code1}\n```\n{text2}\n```python\n{code2}\n```\n{text3}";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(text1));
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(code1));
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(text2));
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(code2));
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(text3));
        var preCount = result.Split("<pre class='generated-code-block'>").Length - 1;
        preCount.Should().Be(2);
    }

    [Test]
    public void FormatReasoningTextWithCode_WithIncompleteCodeBlock_ReturnsOriginalEncoded()
    {
        // Arrange
        var input = "```python\nprint('Hello, World!')";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Be(System.Net.WebUtility.HtmlEncode(input));
        result.Should().NotContain("<pre");
    }

    [Test]
    public void FormatReasoningTextWithCode_WithMixedTextAndCode_MaintainsOrder()
    {
        // Arrange
        var input = "Step 1: Read the file\n```python\nwith open('file.txt') as f:\n    data = f.read()\n```\nStep 2: Process it";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        // Verify order is maintained by checking indices
        var step1Index = result.IndexOf("Step 1");
        var preIndex = result.IndexOf("<pre");
        var step2Index = result.IndexOf("Step 2");
        
        step1Index.Should().BeGreaterThanOrEqualTo(0);
        preIndex.Should().BeGreaterThan(step1Index);
        step2Index.Should().BeGreaterThan(preIndex);
    }

    [Test]
    public void FormatReasoningTextWithCode_WithQuotesInCode_HandlesCorrectly()
    {
        // Arrange
        var codeContent = "print(\"Hello 'World'\")";
        var input = $"```python\n{codeContent}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("&quot;");
        result.Should().Contain("&#39;");
    }

    [Test]
    public void FormatReasoningTextWithCode_PerformanceTest_HandlesLargeInput()
    {
        // Arrange
        var largeCode = string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"line{i} = 'value{i}'"));
        var input = $"Here's a large code block:\n```python\n{largeCode}\n```\nEnd of code.";

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _service.FormatReasoningTextWithCode(input);
        sw.Stop();

        // Assert
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain("line1");
        result.Should().Contain("line1000");
        sw.ElapsedMilliseconds.Should().BeLessThan(100); // Should be fast
    }

    [Test]
    public void FormatReasoningTextWithCode_WithNestedBackticks_HandlesCorrectly()
    {
        // Arrange
        var codeContent = "markdown_text = \"Use `code` in markdown\"";
        var input = $"```python\n{codeContent}\n```";

        // Act
        var result = _service.FormatReasoningTextWithCode(input);

        // Assert
        result.Should().Contain("<pre class='generated-code-block'>");
        result.Should().Contain(System.Net.WebUtility.HtmlEncode(codeContent));
    }
}
