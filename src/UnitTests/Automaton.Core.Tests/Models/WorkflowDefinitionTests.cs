using Automaton.Core.Models;
using Automaton.Core.Tests.Helpers;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class WorkflowDefinitionTests
    {
        [Test]
        public void Constructor_ShouldInitializeStepsCollection()
        {
            // Arrange & Act
            var definition = new WorkflowDefinition();

            // Assert
            Assert.That(definition.Steps, Is.Not.Null);
            Assert.That(definition.Steps.Count, Is.EqualTo(0));
        }

        [Test]
        public void GetFirstStep_WhenStepsExist_ShouldReturnFirstStep()
        {
            // Arrange
            var definition = new WorkflowDefinition();
            var step1 = new TestWorkflowStep { Id = "step1", Name = "First Step" };
            var step2 = new TestWorkflowStep { Id = "step2", Name = "Second Step" };
            
            definition.Steps.Add("step1", step1);
            definition.Steps.Add("step2", step2);

            // Act
            var result = definition.GetFirstStep();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo("step1"));
        }

        [Test]
        public void GetFirstStep_WhenNoSteps_ShouldReturnNull()
        {
            // Arrange
            var definition = new WorkflowDefinition();

            // Act
            var result = definition.GetFirstStep();

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Properties_ShouldBeSettable()
        {
            // Arrange & Act
            var definition = new WorkflowDefinition
            {
                Id = "test-definition"
            };

            // Assert
            Assert.That(definition.Id, Is.EqualTo("test-definition"));
        }
    }
}