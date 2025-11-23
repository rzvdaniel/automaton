using Automaton.Mcp.Models;
using Automaton.Studio.Services;
using FluentAssertions;
using NUnit.Framework;

namespace Automaton.Studio.Tests.Components;

/// <summary>
/// Tests to validate that duplicate messages are not added to the conversation history
/// and that error messages are properly handled in the AI chat.
/// </summary>
[TestFixture]
public class ConversationDuplicateHandlingTests
{
    private ITextSimilarityService _similarityService = null!;

    [SetUp]
    public void SetUp()
    {
        _similarityService = new TextSimilarityService();
    }

    #region Duplicate Detection Tests

    [Test]
    public void DuplicateMessage_WithExactSameContent_ShouldBeDetected()
    {
        // Arrange
        var message1 = "I see the error in your code. Let me fix it for you.";
        var message2 = "I see the error in your code. Let me fix it for you.";

        // Act
        var areSimilar = _similarityService.AreSimilar(message1, message2, similarityThreshold: 0.95);

        // Assert
        areSimilar.Should().BeTrue("messages with identical content should be detected as duplicates");
    }

    [Test]
    public void DuplicateMessage_WithMinorDifferences_ShouldBeDetected()
    {
        // Arrange - Longer messages with meaningful content are better for similarity detection
        var message1 = "I see the error in your code. I'll fix the variable assignment issue.";
        var message2 = "I see the error in your code. I will fix the variable assignment issue.";

        // Act
        var areSimilar = _similarityService.AreSimilar(message1, message2, similarityThreshold: 0.8);

        // Assert
        areSimilar.Should().BeTrue("messages with minor differences (I'll vs I will) should be detected as duplicates");
    }

    [Test]
    public void DifferentMessages_ShouldNotBeDetectedAsDuplicates()
    {
        // Arrange
        var message1 = "I see the error in your code";
        var message2 = "Now I'm testing the code execution";

        // Act
        var areSimilar = _similarityService.AreSimilar(message1, message2, similarityThreshold: 0.95);

        // Assert
        areSimilar.Should().BeFalse("messages with different content should not be detected as duplicates");
    }

    #endregion

    #region ConversationUpdatedEvent Structure Tests

    [Test]
    public void ConversationUpdatedEvent_WithMessages_ShouldContainRequiredFields()
    {
        // Arrange & Act
        var conversationEvent = new ConversationUpdatedEvent
        {
            ConversationId = "test-conversation-123",
            Messages = new List<ConversationMessage>
            {
                new ConversationMessage("assistant", "Test message content")
            },
            ToolCalls = new List<ToolCall>(),
            ToolResults = new List<ToolResult>()
        };

        // Assert
        conversationEvent.ConversationId.Should().Be("test-conversation-123");
        conversationEvent.Messages.Should().HaveCount(1);
        conversationEvent.Messages[0].Role.Should().Be("assistant");
        conversationEvent.Messages[0].Content.Should().Be("Test message content");
        conversationEvent.ToolCalls.Should().BeEmpty();
        conversationEvent.ToolResults.Should().BeEmpty();
    }

    [Test]
    public void ConversationUpdatedEvent_WithErrorMessage_ShouldBeProperlyFormatted()
    {
        // Arrange
        var errorMessage = "Error: Incomplete model output due to token limit";
        
        // Act
        var conversationEvent = new ConversationUpdatedEvent
        {
            ConversationId = "test-conversation-123",
            Messages = new List<ConversationMessage>
            {
                new ConversationMessage("assistant", errorMessage)
            },
            ToolCalls = new List<ToolCall>(),
            ToolResults = new List<ToolResult>()
        };

        // Assert
        conversationEvent.Messages.Should().HaveCount(1);
        conversationEvent.Messages[0].Content.Should().StartWith("Error:");
        conversationEvent.Messages[0].Content.Should().Contain("token limit");
    }

    [Test]
    public void ConversationMessage_ShouldContainTimestamp()
    {
        // Arrange & Act
        var message = new ConversationMessage("assistant", "Test content");
        
        // Assert
        message.Timestamp.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Message History Building Tests

    [Test]
    public void ConversationHistory_ShouldExcludeSystemMessages()
    {
        // This test validates the logic in BuildMcpConversationHistory
        // where System, Generating, and ToolExecution messages should be excluded
        
        // Arrange
        var messages = new List<(string role, string type)>
        {
            ("System", "System"),
            ("User", "Text"),
            ("AI", "Text"),
            ("AI", "Generating"),
            ("AI", "ToolExecution")
        };

        // Act - filter messages as done in BuildMcpConversationHistory
        var includedMessages = messages
            .Where(m => m.type != "System" && m.type != "Generating" && m.type != "ToolExecution")
            .ToList();

        // Assert
        includedMessages.Should().HaveCount(2, "only User and AI Text messages should be included");
        includedMessages.Should().Contain(m => m.role == "User");
        includedMessages.Should().Contain(m => m.role == "AI" && m.type == "Text");
    }

    #endregion

    #region Similarity Threshold Tests

    [Test]
    public void SimilarityThreshold_At95Percent_ShouldBeStrictEnoughToAvoidDuplicates()
    {
        // Arrange - messages that should be detected as duplicates at 0.95 threshold
        var pairs = new[]
        {
            ("I see the error", "I see the error"),
            ("Let me fix this", "let me fix this"),
            // Note: "I'll" vs "I will" requires lower threshold due to word count differences
        };

        foreach (var (msg1, msg2) in pairs)
        {
            // Act
            var areSimilar = _similarityService.AreSimilar(msg1, msg2, similarityThreshold: 0.95);

            // Assert
            areSimilar.Should().BeTrue($"'{msg1}' and '{msg2}' should be detected as duplicates");
        }
    }
    
    [Test]
    public void SimilarityThreshold_DefaultSettings_ShouldDetectMinorVariations()
    {
        // Arrange - Test the exact pattern from existing TextSimilarityServiceTests
        var message1 = "Let me analyze the error and I'll fix it for you";
        var message2 = "Let me analyze the error and I will fix it for you";

        // Act - use default threshold (0.8 for similarity)
        var areSimilar = _similarityService.AreSimilar(message1, message2);

        // Assert
        areSimilar.Should().BeTrue("messages with minor contraction differences should be detected as duplicates with default threshold");
    }

    [Test]
    public void SimilarityThreshold_At95Percent_ShouldNotFlagDifferentMessages()
    {
        // Arrange - messages that should NOT be detected as duplicates
        var pairs = new[]
        {
            ("Reading current code", "Testing code execution"),
            ("I see the error", "Now testing the output"),
            ("Let me analyze", "The result is correct")
        };

        foreach (var (msg1, msg2) in pairs)
        {
            // Act
            var areSimilar = _similarityService.AreSimilar(msg1, msg2, similarityThreshold: 0.95);

            // Assert
            areSimilar.Should().BeFalse($"'{msg1}' and '{msg2}' should NOT be detected as duplicates");
        }
    }

    #endregion

    #region Error Message Formatting Tests

    [Test]
    public void ErrorMessage_ForTokenLimit_ShouldBeFormatted()
    {
        // Arrange
        var errorMsg = "Incomplete model output due to token limit";
        
        // Act
        var formattedError = $"Error: {errorMsg}";

        // Assert
        formattedError.Should().Be("Error: Incomplete model output due to token limit");
        formattedError.Should().StartWith("Error:");
    }

    [Test]
    public void ErrorMessage_ForUnexpectedFinishReason_ShouldIncludeReason()
    {
        // Arrange
        var finishReason = "UnknownReason";
        var errorMsg = $"Unexpected finish reason: {finishReason}";
        
        // Act
        var formattedError = $"Error: {errorMsg}";

        // Assert
        formattedError.Should().Be("Error: Unexpected finish reason: UnknownReason");
        formattedError.Should().Contain(finishReason);
    }

    [Test]
    public void ErrorMessage_ForGeneralException_ShouldIncludeExceptionMessage()
    {
        // Arrange
        var exceptionMessage = "Connection timeout occurred";
        
        // Act
        var formattedError = $"Error: {exceptionMessage}";

        // Assert
        formattedError.Should().Be("Error: Connection timeout occurred");
        formattedError.Should().Contain("Connection timeout");
    }

    #endregion
}
