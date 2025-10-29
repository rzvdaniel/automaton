using Automaton.Core.Enums;
using Automaton.Core.Events;
using Automaton.Core.Models;

namespace Automaton.Core.Tests.Enums
{
    [TestFixture]
    public class VariableTypeTests
    {
        [Test]
        public void VariableType_ShouldHaveExpectedValues()
        {
            // Assert that all expected enum values exist
            Assert.That(Enum.IsDefined(typeof(VariableType), VariableType.String));
        }

        [Test]
        public void VariableType_ShouldHaveCorrectIntegerValues()
        {
            // Verify enum values maintain their expected integer representations
            Assert.That((int)VariableType.String, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class WorkflowStatusTests
    {
        [Test]
        public void WorkflowStatus_ShouldHaveExpectedValues()
        {
            // Assert that expected enum values exist
            Assert.That(Enum.IsDefined(typeof(WorkflowStatus), WorkflowStatus.None));
            Assert.That(Enum.IsDefined(typeof(WorkflowStatus), WorkflowStatus.Working));
            Assert.That(Enum.IsDefined(typeof(WorkflowStatus), WorkflowStatus.Error));
            Assert.That(Enum.IsDefined(typeof(WorkflowStatus), WorkflowStatus.Success));
        }
    }

    [TestFixture]
    public class RunnerStatusTests
    {
        [Test]
        public void RunnerStatus_ShouldHaveExpectedValues()
        {
            // Assert that expected enum values exist
            Assert.That(Enum.IsDefined(typeof(RunnerStatus), RunnerStatus.None));
            Assert.That(Enum.IsDefined(typeof(RunnerStatus), RunnerStatus.Online));
            Assert.That(Enum.IsDefined(typeof(RunnerStatus), RunnerStatus.Offline));
        }
    }

    [TestFixture]
    public class WorkflowErrorHandlingTests
    {
        [Test]
        public void WorkflowErrorHandling_ShouldHaveExpectedValues()
        {
            // Assert that expected enum values exist
            Assert.That(Enum.IsDefined(typeof(WorkflowErrorHandling), WorkflowErrorHandling.Retry));
            Assert.That(Enum.IsDefined(typeof(WorkflowErrorHandling), WorkflowErrorHandling.Suspend));
            Assert.That(Enum.IsDefined(typeof(WorkflowErrorHandling), WorkflowErrorHandling.Terminate));
            Assert.That(Enum.IsDefined(typeof(WorkflowErrorHandling), WorkflowErrorHandling.Compensate));
        }

        [Test]
        public void WorkflowErrorHandling_ShouldHaveCorrectIntegerValues()
        {
            // Verify enum values maintain their expected integer representations
            Assert.That((int)WorkflowErrorHandling.Retry, Is.EqualTo(0));
            Assert.That((int)WorkflowErrorHandling.Suspend, Is.EqualTo(1));
            Assert.That((int)WorkflowErrorHandling.Terminate, Is.EqualTo(2));
            Assert.That((int)WorkflowErrorHandling.Compensate, Is.EqualTo(3));
        }
    }
}

namespace Automaton.Core.Tests.Events
{
    [TestFixture]
    public class SetVariableNotificationTests
    {
        [Test]
        public void Constructor_ShouldSetVariable()
        {
            // Arrange
            var variable = new StepVariable { Id = "test", Name = "TestVar", Value = "testValue" };

            // Act
            var notification = new SetVariableNotification(variable);

            // Assert
            Assert.That(notification.Variable, Is.EqualTo(variable));
            Assert.That(notification.Variable.Name, Is.EqualTo("TestVar"));
            Assert.That(notification.Variable.Value, Is.EqualTo("testValue"));
        }

        [Test]
        public void Variable_Property_ShouldBeReadOnly()
        {
            // Arrange
            var variable = new StepVariable { Id = "test", Name = "TestVar", Value = "testValue" };
            var notification = new SetVariableNotification(variable);

            // Act & Assert - Variable property should not have a public setter
            var propertyInfo = typeof(SetVariableNotification).GetProperty("Variable");
            Assert.That(propertyInfo, Is.Not.Null);
            Assert.That(propertyInfo.CanRead, Is.True);
            Assert.That(propertyInfo.SetMethod?.IsPublic, Is.Not.True);
        }
    }
}