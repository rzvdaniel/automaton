using Automaton.Core.Extensions;
using Automaton.Core.Models;

namespace Automaton.Core.Tests.Extensions
{
    [TestFixture]
    public class JsonExtensionsTests
    {
        [Test]
        public void CloneJson_ShouldCreateDeepCopyOfObject()
        {
            // Arrange
            var original = new Step
            {
                Id = "test-id",
                Name = "test-name",
                DisplayName = "Test Display Name",
                Type = "TestType"
            };

            var inputVariable = new StepVariable { Id = "input1", Name = "Input1", Value = "test" };
            original.Inputs.Add("input1", inputVariable);

            // Act
            var clone = original.CloneJson();

            // Assert
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(clone.Type, Is.EqualTo(original.Type));
            
            // Verify deep copy of inputs
            Assert.That(clone.Inputs.Count, Is.EqualTo(original.Inputs.Count));
            Assert.That(clone.Inputs["input1"], Is.Not.SameAs(original.Inputs["input1"]));
            Assert.That(clone.Inputs["input1"].Name, Is.EqualTo(original.Inputs["input1"].Name));
        }

        [Test]
        public void CloneJson_WithComplexObject_ShouldCloneAllProperties()
        {
            // Arrange
            var original = new Workflow
            {
                Id = Guid.NewGuid(),
                Name = "Test Workflow",
                StartupDefinitionId = "startup"
            };

            var variable = new StepVariable { Id = "var1", Name = "Variable1", Value = "test" };
            original.Variables.Add("var1", variable);

            // Act
            var clone = original.CloneJson();

            // Assert
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.StartupDefinitionId, Is.EqualTo(original.StartupDefinitionId));
            
            // Verify deep copy of variables
            Assert.That(clone.Variables.Count, Is.EqualTo(original.Variables.Count));
            Assert.That(clone.Variables["var1"], Is.Not.SameAs(original.Variables["var1"]));
            Assert.That(clone.Variables["var1"].Name, Is.EqualTo(original.Variables["var1"].Name));
        }

        [Test]
        public void CloneJson_WithNullObject_ShouldReturnDefault()
        {
            // Arrange
            Step? original = null;

            // Act
            var clone = original.CloneJson();

            // Assert
            Assert.That(clone, Is.Null);
        }

        [Test]
        public void CloneJson_ShouldHandleNestedCollections()
        {
            // Arrange
            var original = new Workflow
            {
                Id = Guid.NewGuid(),
                Name = "Test Workflow"
            };

            var definition = new WorkflowDefinition { Id = "def1" };
            original.Definitions.Add(definition);

            // Act
            var clone = original.CloneJson();

            // Assert
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.Definitions.Count, Is.EqualTo(original.Definitions.Count));
            Assert.That(clone.Definitions[0], Is.Not.SameAs(original.Definitions[0]));
            Assert.That(clone.Definitions[0].Id, Is.EqualTo(original.Definitions[0].Id));
        }

        [Test]
        public void CloneJson_WithStepVariable_ShouldCloneValueCorrectly()
        {
            // Arrange
            var original = new StepVariable
            {
                Id = "var1",
                Name = "Variable1",
                Value = "test value",
                Description = "Test description"
            };

            // Act
            var clone = original.CloneJson();

            // Assert
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Value, Is.EqualTo(original.Value));
            Assert.That(clone.Description, Is.EqualTo(original.Description));
        }

        [Test]
        public void CloneJson_WithModificationToClone_ShouldNotAffectOriginal()
        {
            // Arrange
            var original = new Step
            {
                Id = "original-id",
                Name = "original-name"
            };

            // Act
            var clone = original.CloneJson();
            clone.Id = "modified-id";
            clone.Name = "modified-name";

            // Assert
            Assert.That(original.Id, Is.EqualTo("original-id"));
            Assert.That(original.Name, Is.EqualTo("original-name"));
            Assert.That(clone.Id, Is.EqualTo("modified-id"));
            Assert.That(clone.Name, Is.EqualTo("modified-name"));
        }
    }
}