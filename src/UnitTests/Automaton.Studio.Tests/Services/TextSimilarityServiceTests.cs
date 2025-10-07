using Automaton.Studio.Services;
using FluentAssertions;
using NUnit.Framework;

namespace Automaton.Studio.Tests.Services;

[TestFixture]
public class TextSimilarityServiceTests
{
    private TextSimilarityService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new TextSimilarityService();
    }

    #region Exact Match Tests

    [Test]
    public void AreSimilar_ExactMatch_ReturnsTrue()
    {
        // Arrange
        var text1 = "This is a test message";
        var text2 = "This is a test message";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_ExactMatchWithDifferentWhitespace_ReturnsTrue()
    {
        // Arrange
        var text1 = "This   is   a test    message";
        var text2 = "This is a test message";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Length Ratio Tests

    [Test]
    public void AreSimilar_VeryDifferentLengths_ReturnsFalse()
    {
        // Arrange
        var text1 = "Short";
        var text2 = "This is a much longer text that should not be considered similar due to length difference";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void AreSimilar_AcceptableLengthDifference_ConsidersOtherFactors()
    {
        // Arrange
        var text1 = "I see the error in the code and I'll fix it";
        var text2 = "I see the error in the code and I will fix it now";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue("high word overlap should make them similar");
    }

    [Test]
    public void AreSimilar_CustomLengthRatioThreshold_RespectsThreshold()
    {
        // Arrange - use texts with partial word overlap but NO substring matches
        var text1 = "Red car fast";                    // 3 words, ~13 chars  
        var text2 = "Big red car drives very fast";    // 6 words, ~27 chars
        
        // Length ratio = 13/27 ? 0.48
        // Word overlap = 3/6 = 0.5 (below default 0.8 threshold)
        // Not a substring match (text1 is NOT contained in text2 as continuous text)

        // Act
        var resultStrict = _service.AreSimilar(text1, text2, lengthRatioThreshold: 0.9); // Requires 90% length similarity
        var resultLenient = _service.AreSimilar(text1, text2, lengthRatioThreshold: 0.4); // Requires 40% length similarity

        // Assert
        resultStrict.Should().BeFalse("strict length ratio threshold (0.9) should reject texts with 48% length ratio");
        resultLenient.Should().BeFalse("even with lenient threshold (0.4), low word overlap (50%) is below similarity threshold (80%)");
    }

    #endregion

    #region Substring Match Tests

    [Test]
    public void AreSimilar_OneContainsOther_ReturnsTrue()
    {
        // Arrange
        var text1 = "I'll fix the error";
        var text2 = "I'll fix the error in your code";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_SubstringWithDifferentCase_ReturnsTrue()
    {
        // Arrange
        var text1 = "Fix The Error";
        var text2 = "fix the error in code";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Word Overlap Tests

    [Test]
    public void AreSimilar_HighWordOverlap_ReturnsTrue()
    {
        // Arrange
        var text1 = "The error is in line 42 of the code";
        var text2 = "The error is found in line 42 of code";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_LowWordOverlap_ReturnsFalse()
    {
        // Arrange
        var text1 = "The quick brown fox jumps";
        var text2 = "Lazy dogs sleep under trees";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void AreSimilar_CustomSimilarityThreshold_RespectsThreshold()
    {
        // Arrange
        var text1 = "I will fix the error in your code now";
        var text2 = "I will fix the error in code";

        // Act
        var resultStrict = _service.AreSimilar(text1, text2, similarityThreshold: 0.95);
        var resultLenient = _service.AreSimilar(text1, text2, similarityThreshold: 0.7);

        // Assert
        resultStrict.Should().BeFalse();
        resultLenient.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_ExactlyAtThreshold_ReturnsTrue()
    {
        // Arrange - craft text with exactly 80% overlap
        var text1 = "word1 word2 word3 word4 word5"; // 5 words
        var text2 = "word1 word2 word3 word4 different"; // 4 common words = 80%

        // Act
        var result = _service.AreSimilar(text1, text2, similarityThreshold: 0.8);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void AreSimilar_EmptyStrings_ReturnsFalse()
    {
        // Arrange
        var text1 = "";
        var text2 = "";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void AreSimilar_OneEmptyString_ReturnsFalse()
    {
        // Arrange
        var text1 = "Some content";
        var text2 = "";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void AreSimilar_WhitespaceOnly_ReturnsFalse()
    {
        // Arrange
        var text1 = "   ";
        var text2 = "\t\n  ";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void AreSimilar_NullStrings_ReturnsFalse()
    {
        // Arrange
        string? text1 = null;
        string? text2 = null;

        // Act
        var result = _service.AreSimilar(text1!, text2!);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void AreSimilar_SingleWord_ReturnsTrueIfSame()
    {
        // Arrange
        var text1 = "Error";
        var text2 = "Error";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_SingleWord_ReturnsFalseIfDifferent()
    {
        // Arrange
        var text1 = "Error";
        var text2 = "Success";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Real-World Scenarios

    [Test]
    public void AreSimilar_AiReasoningMessages_DetectsSimilarThinking()
    {
        // Arrange - simulates AI messages that might appear similar
        var text1 = "I see the error in your code. The issue is with the variable assignment on line 42.";
        var text2 = "I see the error in your code. The issue is the variable assignment on line 42.";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_DifferentAiMessages_NotSimilar()
    {
        // Arrange
        var text1 = "I see the error in your code. Let me fix the syntax issue.";
        var text2 = "Now I'm going to test the code execution to verify it works.";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void AreSimilar_TypicalChatDuplicates_DetectedCorrectly()
    {
        // Arrange
        var text1 = "Let me analyze the error and I'll fix it for you";
        var text2 = "Let me analyze the error and I will fix it for you";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_CodeSnippetsWithMinorDifferences_DetectedAsSimilar()
    {
        // Arrange
        var text1 = "The function should return the result of the calculation";
        var text2 = "The function should return result of calculation";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_CompletelyDifferentContent_NotSimilar()
    {
        // Arrange
        var text1 = "Reading current code from editor to analyze the Python script";
        var text2 = "Testing code execution to verify the output is correct";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Case Sensitivity Tests

    [Test]
    public void AreSimilar_DifferentCase_StillSimilar()
    {
        // Arrange
        var text1 = "THIS IS A TEST MESSAGE";
        var text2 = "this is a test message";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void AreSimilar_MixedCase_ConsideredSimilar()
    {
        // Arrange
        var text1 = "I Will Fix The Error";
        var text2 = "i will fix the error";

        // Act
        var result = _service.AreSimilar(text1, text2);

        // Assert
        result.Should().BeTrue();
    }

    #endregion
}
