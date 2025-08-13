using Automaton.Core.Models;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class StepTests
    {
        [Test]
        public void Constructor_ShouldInitializeCollections()
        {
            // Arrange & Act
            var step = new Step();

            // Assert
            Assert.That(step.Inputs, Is.Not.Null);
            Assert.That(step.Outputs, Is.Not.Null);
            Assert.That(step.Inputs.Count, Is.EqualTo(0));
            Assert.That(step.Outputs.Count, Is.EqualTo(0));
        }

        [Test]
        public void Properties_ShouldBeSettableAndGettable()
        {
            // Arrange & Act
            var step = new Step
            {
                Id = "test-id",
                Name = "test-name",
                DisplayName = "Test Display Name",
                Description = "Test description",
                Type = "TestType",
                MoreInfo = "More information",
                Category = "TestCategory",
                VisibleInExplorer = true,
                Icon = "test-icon",
                NextStepId = "next-step-id",
                ParentId = "parent-id"
            };

            // Assert
            Assert.That(step.Id, Is.EqualTo("test-id"));
            Assert.That(step.Name, Is.EqualTo("test-name"));
            Assert.That(step.DisplayName, Is.EqualTo("Test Display Name"));
            Assert.That(step.Description, Is.EqualTo("Test description"));
            Assert.That(step.Type, Is.EqualTo("TestType"));
            Assert.That(step.MoreInfo, Is.EqualTo("More information"));
            Assert.That(step.Category, Is.EqualTo("TestCategory"));
            Assert.That(step.VisibleInExplorer, Is.True);
            Assert.That(step.Icon, Is.EqualTo("test-icon"));
            Assert.That(step.NextStepId, Is.EqualTo("next-step-id"));
            Assert.That(step.ParentId, Is.EqualTo("parent-id"));
        }

        [Test]
        public void Inputs_ShouldAllowAddingStepVariables()
        {
            // Arrange
            var step = new Step();
            var inputVariable = new StepVariable { Id = "input1", Name = "Input1", Value = "test" };

            // Act
            step.Inputs.Add("input1", inputVariable);

            // Assert
            Assert.That(step.Inputs.Count, Is.EqualTo(1));
            Assert.That(step.Inputs["input1"], Is.EqualTo(inputVariable));
        }

        [Test]
        public void Outputs_ShouldAllowAddingStepVariables()
        {
            // Arrange
            var step = new Step();
            var outputVariable = new StepVariable { Id = "output1", Name = "Output1", Value = "result" };

            // Act
            step.Outputs.Add("output1", outputVariable);

            // Assert
            Assert.That(step.Outputs.Count, Is.EqualTo(1));
            Assert.That(step.Outputs["output1"], Is.EqualTo(outputVariable));
        }

        [Test]
        public void OptionalProperties_ShouldAllowNullValues()
        {
            // Arrange & Act
            var step = new Step
            {
                Id = "required-id",
                Name = "required-name",
                DisplayName = "required-display-name",
                Type = "required-type",
                Icon = "required-icon",
                Description = null,
                MoreInfo = null,
                Category = null,
                NextStepId = null,
                ParentId = null
            };

            // Assert
            Assert.That(step.Description, Is.Null);
            Assert.That(step.MoreInfo, Is.Null);
            Assert.That(step.Category, Is.Null);
            Assert.That(step.NextStepId, Is.Null);
            Assert.That(step.ParentId, Is.Null);
        }
    }
}