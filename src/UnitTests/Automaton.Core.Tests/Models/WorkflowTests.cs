using Automaton.Core.Enums;
using Automaton.Core.Events;
using Automaton.Core.Models;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class WorkflowTests
    {
        private Workflow _workflow;

        [SetUp]
        public void Setup()
        {
            _workflow = new Workflow();
        }

        [Test]
        public void Constructor_ShouldInitializeCollections()
        {
            // Assert
            Assert.That(_workflow.Variables, Is.Not.Null);
            Assert.That(_workflow.OutputVariables, Is.Not.Null);
            Assert.That(_workflow.InputVariables, Is.Not.Null);
            Assert.That(_workflow.Definitions, Is.Not.Null);
            Assert.That(_workflow.Variables.Count, Is.EqualTo(0));
            Assert.That(_workflow.OutputVariables.Count, Is.EqualTo(0));
            Assert.That(_workflow.InputVariables.Count, Is.EqualTo(0));
            Assert.That(_workflow.Definitions.Count, Is.EqualTo(0));
        }

        [Test]
        public void VariableWithNameExists_WhenVariableExists_ShouldReturnTrue()
        {
            // Arrange
            var variable = new StepVariable { Id = "1", Name = "TestVar", Value = "test" };
            _workflow.Variables.Add("1", variable);

            // Act
            var result = _workflow.VariableWithNameExists("TestVar");

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void VariableWithNameExists_WhenVariableDoesNotExist_ShouldReturnFalse()
        {
            // Act
            var result = _workflow.VariableWithNameExists("NonExistentVar");

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void GetVariableByName_WhenVariableExists_ShouldReturnVariable()
        {
            // Arrange
            var variable = new StepVariable { Id = "1", Name = "TestVar", Value = "test" };
            _workflow.Variables.Add("1", variable);

            // Act
            var result = _workflow.GetVariableByName("TestVar");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("TestVar"));
            Assert.That(result.Value, Is.EqualTo("test"));
        }

        [Test]
        public void GetVariableByName_WhenVariableDoesNotExist_ShouldReturnNull()
        {
            // Act
            var result = _workflow.GetVariableByName("NonExistentVar");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetVariables_WhenVariablesExist_ShouldReturnMatchingVariables()
        {
            // Arrange
            var variable1 = new StepVariable { Id = "1", Name = "Var1", Value = "value1" };
            var variable2 = new StepVariable { Id = "2", Name = "Var2", Value = "value2" };
            var variable3 = new StepVariable { Id = "3", Name = "Var3", Value = "value3" };
            
            _workflow.Variables.Add("1", variable1);
            _workflow.Variables.Add("2", variable2);
            _workflow.Variables.Add("3", variable3);

            var requestedNames = new[] { "1", "3" };

            // Act
            var result = _workflow.GetVariables(requestedNames);

            // Assert
            Assert.That(result.Count(), Is.EqualTo(2));
            Assert.That(result.Select(v => v.Id), Does.Contain("1"));
            Assert.That(result.Select(v => v.Id), Does.Contain("3"));
        }

        [Test]
        public void GetVariables_WhenNoVariablesMatch_ShouldReturnEmpty()
        {
            // Arrange
            var variable1 = new StepVariable { Id = "1", Name = "Var1", Value = "value1" };
            _workflow.Variables.Add("1", variable1);

            var requestedNames = new[] { "nonexistent" };

            // Act
            var result = _workflow.GetVariables(requestedNames);

            // Assert
            Assert.That(result.Count(), Is.EqualTo(0));
        }

        [Test]
        public void SetVariable_WhenVariableExists_ShouldUpdateVariable()
        {
            // Arrange
            var originalVariable = new StepVariable { Id = "1", Name = "TestVar", Value = "original" };
            _workflow.Variables.Add("1", originalVariable);

            var updatedVariable = new StepVariable { Id = "1", Name = "TestVar", Value = "updated" };

            // Act
            _workflow.SetVariable(updatedVariable);

            // Assert
            Assert.That(_workflow.Variables["1"].Value, Is.EqualTo("updated"));
        }

        [Test]
        public void SetVariable_WhenVariableDoesNotExist_ShouldAddVariable()
        {
            // Arrange
            var newVariable = new StepVariable { Id = "1", Name = "NewVar", Value = "new" };

            // Act
            _workflow.SetVariable(newVariable);

            // Assert
            Assert.That(_workflow.Variables.ContainsKey("NewVar"), Is.True);
            Assert.That(_workflow.Variables["NewVar"].Value, Is.EqualTo("new"));
        }

        [Test]
        public void SetVariable_ShouldTriggerEvent()
        {
            // Arrange
            var eventTriggered = false;
            SetVariableNotification? capturedNotification = null;
            
            _workflow.SetWorkflowVariable += (sender, notification) =>
            {
                eventTriggered = true;
                capturedNotification = notification;
            };

            var variable = new StepVariable { Id = "1", Name = "TestVar", Value = "test" };

            // Act
            _workflow.SetVariable(variable);

            // Assert
            Assert.That(eventTriggered, Is.True);
            Assert.That(capturedNotification, Is.Not.Null);
            Assert.That(capturedNotification.Variable.Name, Is.EqualTo("TestVar"));
        }

        [Test]
        public void GetStartupDefinition_WhenStartupDefinitionExists_ShouldReturnDefinition()
        {
            // Arrange
            var definition1 = new WorkflowDefinition { Id = "def1" };
            var definition2 = new WorkflowDefinition { Id = "def2" };
            
            _workflow.Definitions.Add(definition1);
            _workflow.Definitions.Add(definition2);
            _workflow.StartupDefinitionId = "def2";

            // Act
            var result = _workflow.GetStartupDefinition();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo("def2"));
        }

        [Test]
        public void GetStartupDefinition_WhenStartupDefinitionDoesNotExist_ShouldReturnNull()
        {
            // Arrange
            var definition1 = new WorkflowDefinition { Id = "def1" };
            _workflow.Definitions.Add(definition1);
            _workflow.StartupDefinitionId = "nonexistent";

            // Act
            var result = _workflow.GetStartupDefinition();

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetStartupDefinition_WhenNoDefinitions_ShouldReturnNull()
        {
            // Arrange
            _workflow.StartupDefinitionId = "def1";

            // Act
            var result = _workflow.GetStartupDefinition();

            // Assert
            Assert.That(result, Is.Null);
        }
    }
}