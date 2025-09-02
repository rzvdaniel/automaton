using Automaton.Core.Extensions;
using Automaton.Core.Logs;
using Serilog.Events;
using NSubstitute;

namespace Automaton.Core.Tests.Extensions
{
    [TestFixture]
    public class EventLogExtensionsTests
    {
        [Test]
        public void IsWorkflowExecution_WithWorkflowExecutionIdProperty_ShouldReturnTrue()
        {
            // Arrange
            var logEvent = Substitute.For<LogEvent>();
            var properties = new Dictionary<string, LogEventPropertyValue>
            {
                { LogContextProperties.WorkflowExecutionId, Substitute.For<LogEventPropertyValue>() }
            };
            logEvent.Properties.Returns(properties);

            // Act
            var result = logEvent.IsWorkflowExecution();

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void IsWorkflowExecution_WithoutWorkflowExecutionIdProperty_ShouldReturnFalse()
        {
            // Arrange
            var logEvent = Substitute.For<LogEvent>();
            var properties = new Dictionary<string, LogEventPropertyValue>
            {
                { LogContextProperties.WorkflowId, Substitute.For<LogEventPropertyValue>() },
                { LogContextProperties.WorkflowName, Substitute.For<LogEventPropertyValue>() }
            };
            logEvent.Properties.Returns(properties);

            // Act
            var result = logEvent.IsWorkflowExecution();

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void IsWorkflowExecution_WithEmptyProperties_ShouldReturnFalse()
        {
            // Arrange
            var logEvent = Substitute.For<LogEvent>();
            var properties = new Dictionary<string, LogEventPropertyValue>();
            logEvent.Properties.Returns(properties);

            // Act
            var result = logEvent.IsWorkflowExecution();

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void IsWorkflowExecution_WithNullProperties_ShouldReturnFalse()
        {
            // Arrange
            var logEvent = Substitute.For<LogEvent>();
            logEvent.Properties.Returns((IReadOnlyDictionary<string, LogEventPropertyValue>)null!);

            // Act & Assert
            Assert.Throws<NullReferenceException>(() => logEvent.IsWorkflowExecution());
        }

        [Test]
        public void IsWorkflowExecution_WithMultiplePropertiesIncludingWorkflowExecutionId_ShouldReturnTrue()
        {
            // Arrange
            var logEvent = Substitute.For<LogEvent>();
            var properties = new Dictionary<string, LogEventPropertyValue>
            {
                { LogContextProperties.WorkflowId, Substitute.For<LogEventPropertyValue>() },
                { LogContextProperties.WorkflowExecutionId, Substitute.For<LogEventPropertyValue>() },
                { LogContextProperties.WorkflowName, Substitute.For<LogEventPropertyValue>() },
                { LogContextProperties.StepId, Substitute.For<LogEventPropertyValue>() },
                { LogContextProperties.StepName, Substitute.For<LogEventPropertyValue>() }
            };
            logEvent.Properties.Returns(properties);

            // Act
            var result = logEvent.IsWorkflowExecution();

            // Assert
            Assert.That(result, Is.True);
        }
    }
}