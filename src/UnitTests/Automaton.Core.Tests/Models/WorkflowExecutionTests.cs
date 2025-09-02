using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class WorkflowExecutionTests
    {
        [Test]
        public void Constructor_ShouldInitializePropertiesCorrectly()
        {
            // Arrange
            var flowId = Guid.NewGuid();
            var application = "TestApp";
            var beforeCreation = DateTime.UtcNow;

            // Act
            var execution = new WorkflowExecution(flowId, application);
            var afterCreation = DateTime.UtcNow;

            // Assert
            Assert.That(execution.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(execution.FlowId, Is.EqualTo(flowId));
            Assert.That(execution.Application, Is.EqualTo(application));
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Working));
            Assert.That(execution.Started, Is.GreaterThanOrEqualTo(beforeCreation));
            Assert.That(execution.Started, Is.LessThanOrEqualTo(afterCreation));
            Assert.That(execution.Finished, Is.EqualTo(default(DateTime)));
        }

        [Test]
        public void HasErrors_ShouldSetInternalErrorFlag()
        {
            // Arrange
            var execution = new WorkflowExecution(Guid.NewGuid(), "TestApp");

            // Act
            execution.HasErrors();

            // Assert - Can't directly test the private field, but can test the effect via Dispose
            execution.Dispose();
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Error));
        }

        [Test]
        public void Dispose_WithoutErrors_ShouldSetSuccessStatus()
        {
            // Arrange
            var execution = new WorkflowExecution(Guid.NewGuid(), "TestApp");
            var beforeDispose = DateTime.UtcNow;

            // Act
            execution.Dispose();
            var afterDispose = DateTime.UtcNow;

            // Assert
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Success));
            Assert.That(execution.Finished, Is.GreaterThanOrEqualTo(beforeDispose));
            Assert.That(execution.Finished, Is.LessThanOrEqualTo(afterDispose));
            Assert.That(execution.Finished, Is.GreaterThanOrEqualTo(execution.Started));
        }

        [Test]
        public void Dispose_WithErrors_ShouldSetErrorStatus()
        {
            // Arrange
            var execution = new WorkflowExecution(Guid.NewGuid(), "TestApp");
            execution.HasErrors();
            var beforeDispose = DateTime.UtcNow;

            // Act
            execution.Dispose();
            var afterDispose = DateTime.UtcNow;

            // Assert
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Error));
            Assert.That(execution.Finished, Is.GreaterThanOrEqualTo(beforeDispose));
            Assert.That(execution.Finished, Is.LessThanOrEqualTo(afterDispose));
            Assert.That(execution.Finished, Is.GreaterThanOrEqualTo(execution.Started));
        }

        [Test]
        public void UsingPattern_ShouldAutoDisposeAndSetStatus()
        {
            // Arrange
            var flowId = Guid.NewGuid();
            WorkflowExecution execution;

            // Act
            using (execution = new WorkflowExecution(flowId, "TestApp"))
            {
                Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Working));
                Assert.That(execution.Finished, Is.EqualTo(default(DateTime)));
            }

            // Assert
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Success));
            Assert.That(execution.Finished, Is.Not.EqualTo(default(DateTime)));
        }

        [Test]
        public void UsingPattern_WithErrors_ShouldAutoDisposeWithErrorStatus()
        {
            // Arrange
            var flowId = Guid.NewGuid();
            WorkflowExecution execution;

            // Act
            using (execution = new WorkflowExecution(flowId, "TestApp"))
            {
                execution.HasErrors();
                Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Working));
            }

            // Assert
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Error));
        }

        [Test]
        public void MultipleHasErrors_ShouldStillResultInErrorStatus()
        {
            // Arrange
            var execution = new WorkflowExecution(Guid.NewGuid(), "TestApp");

            // Act
            execution.HasErrors();
            execution.HasErrors();
            execution.HasErrors();
            execution.Dispose();

            // Assert
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Error));
        }

        [Test]
        public void Constructor_WithEmptyApplication_ShouldWork()
        {
            // Arrange & Act
            var execution = new WorkflowExecution(Guid.NewGuid(), string.Empty);

            // Assert
            Assert.That(execution.Application, Is.EqualTo(string.Empty));
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Working));
        }

        [Test]
        public void Properties_ShouldBeSettableAndGettable()
        {
            // Arrange
            var execution = new WorkflowExecution(Guid.NewGuid(), "TestApp");
            var newId = Guid.NewGuid();
            var newFlowId = Guid.NewGuid();
            var newStarted = DateTime.UtcNow.AddMinutes(-5);
            var newFinished = DateTime.UtcNow;

            // Act
            execution.Id = newId;
            execution.FlowId = newFlowId;
            execution.Started = newStarted;
            execution.Finished = newFinished;
            execution.Status = WorkflowStatus.Error;
            execution.Application = "UpdatedApp";

            // Assert
            Assert.That(execution.Id, Is.EqualTo(newId));
            Assert.That(execution.FlowId, Is.EqualTo(newFlowId));
            Assert.That(execution.Started, Is.EqualTo(newStarted));
            Assert.That(execution.Finished, Is.EqualTo(newFinished));
            Assert.That(execution.Status, Is.EqualTo(WorkflowStatus.Error));
            Assert.That(execution.Application, Is.EqualTo("UpdatedApp"));
        }
    }
}