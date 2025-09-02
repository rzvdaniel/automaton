using Automaton.Core.Exceptions;

namespace Automaton.Core.Tests.Exceptions
{
    [TestFixture]
    public class WorkflowDefinitionLoadExceptionTests
    {
        [Test]
        public void Constructor_WithMessage_ShouldSetMessage()
        {
            // Arrange
            var message = "Test workflow definition load error";

            // Act
            var exception = new WorkflowDefinitionLoadException(message);

            // Assert
            Assert.That(exception.Message, Is.EqualTo(message));
        }

        [Test]
        public void Constructor_WithEmptyMessage_ShouldSetEmptyMessage()
        {
            // Arrange
            var message = string.Empty;

            // Act
            var exception = new WorkflowDefinitionLoadException(message);

            // Assert
            Assert.That(exception.Message, Is.EqualTo(message));
        }

        [Test]
        public void Constructor_WithNullMessage_ShouldSetNullMessage()
        {
            // Act
            var exception = new WorkflowDefinitionLoadException(null!);

            // Assert
            Assert.That(exception.Message, Is.Null);
        }

        [Test]
        public void Exception_ShouldInheritFromException()
        {
            // Arrange
            var exception = new WorkflowDefinitionLoadException("test");

            // Act & Assert
            Assert.That(exception, Is.InstanceOf<Exception>());
        }

        [Test]
        public void Exception_ShouldBeThrowable()
        {
            // Arrange
            var message = "This is a test exception";

            // Act & Assert
            var exception = Assert.Throws<WorkflowDefinitionLoadException>(() =>
            {
                throw new WorkflowDefinitionLoadException(message);
            });

            Assert.That(exception.Message, Is.EqualTo(message));
        }

        [Test]
        public void Exception_WithComplexMessage_ShouldPreserveMessage()
        {
            // Arrange
            var message = "Failed to load workflow definition from file 'test.json'. " +
                         "Error: Invalid JSON format. Line 5, Position 10.";

            // Act
            var exception = new WorkflowDefinitionLoadException(message);

            // Assert
            Assert.That(exception.Message, Is.EqualTo(message));
        }

        [Test]
        public void Exception_ShouldHaveProperStackTrace()
        {
            // Act & Assert
            var exception = Assert.Throws<WorkflowDefinitionLoadException>(() =>
            {
                ThrowWorkflowDefinitionLoadException();
            });

            Assert.That(exception.StackTrace, Is.Not.Null);
            Assert.That(exception.StackTrace, Does.Contain(nameof(ThrowWorkflowDefinitionLoadException)));
        }

        [Test]
        public void Exception_MultipleInstances_ShouldHaveIndependentMessages()
        {
            // Arrange
            var message1 = "First error";
            var message2 = "Second error";

            // Act
            var exception1 = new WorkflowDefinitionLoadException(message1);
            var exception2 = new WorkflowDefinitionLoadException(message2);

            // Assert
            Assert.That(exception1.Message, Is.EqualTo(message1));
            Assert.That(exception2.Message, Is.EqualTo(message2));
            Assert.That(exception1.Message, Is.Not.EqualTo(exception2.Message));
        }

        private void ThrowWorkflowDefinitionLoadException()
        {
            throw new WorkflowDefinitionLoadException("Test exception for stack trace");
        }
    }
}