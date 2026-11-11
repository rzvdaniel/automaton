using Automaton.Core.Enums;
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
}
