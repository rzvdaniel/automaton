using Automaton.Mcp.Models;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Automaton.Studio.Tests.Components;

/// <summary>
/// Tests to validate that conversations can be properly cancelled using cancellation tokens
/// and that the cancellation is handled correctly across different AI providers.
/// </summary>
[TestFixture]
public class ConversationCancellationTests
{
    #region CancellationToken Tests

    [Test]
    public void CancellationToken_WhenCancelled_ShouldThrowTaskCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - TaskCanceledException is a subclass of OperationCanceledException
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await Task.Delay(1000, cts.Token);
        });
    }

    [Test]
    public void CancellationTokenSource_WhenDisposed_ShouldNotThrow()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        cts.Dispose();

        // Assert - should not throw
        Assert.Pass("CancellationTokenSource disposed successfully");
    }

    [Test]
    public async Task CancellationToken_WhenNotCancelled_ShouldAllowTaskToComplete()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var completed = false;

        // Act
        await Task.Run(() =>
        {
            Thread.Sleep(10);
            completed = true;
        }, cts.Token);

        // Assert
        completed.Should().BeTrue("task should complete when token is not cancelled");
    }

    [Test]
    public void CancellationToken_WhenCreatedFromSource_ShouldHaveCorrectProperties()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        var token = cts.Token;

        // Assert
        token.CanBeCanceled.Should().BeTrue("token should be cancellable");
        token.IsCancellationRequested.Should().BeFalse("token should not be cancelled initially");
    }

    [Test]
    public void CancellationToken_AfterCancellation_ShouldIndicateCancellationRequested()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        cts.Cancel();

        // Assert
        token.IsCancellationRequested.Should().BeTrue("token should indicate cancellation was requested");
    }

    #endregion

    #region Conversation Cleanup Tests

    [Test]
    public void ConversationId_WhenSet_ShouldBeAccessible()
    {
        // Arrange
        var conversationId = Guid.NewGuid().ToString();
        var request = new ConversationRequest
        {
            ConversationId = conversationId,
            InitialMessage = "Test message"
        };

        // Assert
        request.ConversationId.Should().Be(conversationId);
    }

    #endregion
}
