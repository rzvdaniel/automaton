using Automaton.Core.Models;
using Automaton.Core.Attributes;
using Automaton.Core.Enums;
using Newtonsoft.Json.Linq;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class WorkflowStepTests
    {
        private TestWorkflowStepForWorkflowStep _workflowStep;
        private WorkflowDefinition _workflowDefinition;
        private Workflow _workflow;

        [SetUp]
        public void Setup()
        {
            _workflowStep = new TestWorkflowStepForWorkflowStep();
            _workflowDefinition = new WorkflowDefinition { Id = "test-def" };
            _workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test Workflow" };
        }

        [Test]
        public void Constructor_ShouldInitializeCollections()
        {
            // Act
            var step = new TestWorkflowStepForWorkflowStep();

            // Assert
            Assert.That(step.Inputs, Is.Not.Null);
            Assert.That(step.Outputs, Is.Not.Null);
            Assert.That(step.Inputs.Count, Is.EqualTo(0));
            Assert.That(step.Outputs.Count, Is.EqualTo(0));
        }

        [Test]
        public void Setup_ShouldCopyStepPropertiesCorrectly()
        {
            // Arrange
            var step = new Step
            {
                Id = "step-1",
                Name = "Test Step",
                DisplayName = "Display Name",
                Description = "Description",
                Type = "TestType",
                NextStepId = "next-step",
                ParentId = "parent-step"
            };
            step.Inputs.Add("input1", new StepVariable { Id = "input1", Name = "TestInput", Value = "test" });
            step.Outputs.Add("output1", new StepVariable { Id = "output1", Name = "TestOutput", Value = "result" });

            // Act
            _workflowStep.Setup(step, _workflowDefinition);

            // Assert
            Assert.That(_workflowStep.Id, Is.EqualTo("step-1"));
            Assert.That(_workflowStep.Name, Is.EqualTo("Test Step"));
            Assert.That(_workflowStep.DisplayName, Is.EqualTo("Display Name"));
            Assert.That(_workflowStep.Description, Is.EqualTo("Description"));
            Assert.That(_workflowStep.Type, Is.EqualTo("TestType"));
            Assert.That(_workflowStep.NextStepId, Is.EqualTo("next-step"));
            Assert.That(_workflowStep.ParentId, Is.EqualTo("parent-step"));
            Assert.That(_workflowStep.WorkflowDefinition, Is.EqualTo(_workflowDefinition));
            Assert.That(_workflowStep.Inputs.Count, Is.EqualTo(1));
            Assert.That(_workflowStep.Outputs.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ExecuteAsync_ShouldCallRunAsyncAndLogExecution()
        {
            // Arrange
            _workflowStep.Setup(new Step { Id = "test", Name = "Test Step" }, _workflowDefinition);
            var context = new StepExecutionContext
            {
                Workflow = _workflow,
                Definition = _workflowDefinition,
                Step = _workflowStep,
                CancellationToken = CancellationToken.None
            };

            // Act
            var result = await _workflowStep.ExecuteAsync(context);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(_workflowStep.RunAsyncCalled, Is.True);
        }

        [Test]
        public void GetNextStep_WhenNextStepIdExists_ShouldReturnNextStep()
        {
            // Arrange
            var nextStep = new TestWorkflowStepForWorkflowStep { Id = "next-step" };
            _workflowDefinition.Steps.Add("next-step", nextStep);
            _workflowStep.NextStepId = "next-step";
            _workflowStep.WorkflowDefinition = _workflowDefinition;

            // Act
            var result = _workflowStep.GetNextStep();

            // Assert
            Assert.That(result, Is.EqualTo(nextStep));
        }

        [Test]
        public void GetNextStep_WhenNextStepIdIsNull_ShouldReturnNull()
        {
            // Arrange
            _workflowStep.NextStepId = null;
            _workflowStep.WorkflowDefinition = _workflowDefinition;

            // Act
            var result = _workflowStep.GetNextStep();

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void SetOutputVariable_ShouldUpdateVariableAndWorkflow()
        {
            // Arrange
            var outputVar = new StepVariable { Id = "output1", Name = "TestOutput", Value = "initial" };
            _workflowStep.Outputs.Add("output1", outputVar);
            var newValue = "updated";

            // Act
            _workflowStep.SetOutputVariable("output1", newValue, _workflow);

            // Assert
            Assert.That(outputVar.Value, Is.EqualTo(newValue));
            Assert.That(_workflow.Variables.ContainsKey("TestOutput"), Is.True);
            Assert.That(_workflow.Variables["TestOutput"].Value, Is.EqualTo(newValue));
        }

        [Test]
        public void SetupProperties_WithStringProperty_ShouldSetPropertyValue()
        {
            // Arrange
            var inputVar = new StepVariable { Name = "TestStringProperty", Value = "test value", Type = VariableType.String };
            _workflowStep.Inputs.Add("input1", inputVar);
            var context = new StepExecutionContext 
            { 
                Workflow = _workflow,
                Definition = _workflowDefinition,
                Step = _workflowStep,
                CancellationToken = CancellationToken.None
            };

            // Act
            _workflowStep.TestSetupProperties(context);

            // Assert
            Assert.That(_workflowStep.TestStringProperty, Is.EqualTo("test value"));
        }

        [Test]
        public void SetupProperties_WithGuidProperty_ShouldParseAndSetGuid()
        {
            // Arrange
            var guidValue = Guid.NewGuid();
            var inputVar = new StepVariable { Name = "TestGuidProperty", Value = guidValue.ToString(), Type = VariableType.String };
            _workflowStep.Inputs.Add("input1", inputVar);
            var context = new StepExecutionContext 
            { 
                Workflow = _workflow,
                Definition = _workflowDefinition,
                Step = _workflowStep,
                CancellationToken = CancellationToken.None
            };

            // Act
            _workflowStep.TestSetupProperties(context);

            // Assert
            Assert.That(_workflowStep.TestGuidProperty, Is.EqualTo(guidValue));
        }

        [Test]
        public void SetupProperties_WithIgnorePropertyParsing_ShouldNotParse()
        {
            // Arrange
            _workflow.Variables.Add("TestVar", new StepVariable { Name = "TestVar", Value = "workflow value" });
            var inputVar = new StepVariable { Name = "IgnoredProperty", Value = "%TestVar%", Type = VariableType.String };
            _workflowStep.Inputs.Add("input1", inputVar);
            var context = new StepExecutionContext 
            { 
                Workflow = _workflow,
                Definition = _workflowDefinition,
                Step = _workflowStep,
                CancellationToken = CancellationToken.None
            };

            // Act
            _workflowStep.TestSetupProperties(context);

            // Assert - Should not parse %TestVar% and should keep the literal value
            Assert.That(_workflowStep.IgnoredProperty, Is.EqualTo("%TestVar%"));
        }
    }

    // Test implementation for WorkflowStep with test properties
    public class TestWorkflowStepForWorkflowStep : WorkflowStep
    {
        public bool RunAsyncCalled { get; private set; }
        public string TestStringProperty { get; set; } = string.Empty;
        public Guid TestGuidProperty { get; set; }
        
        [IgnorePropertyParsing(true)]
        public string IgnoredProperty { get; set; } = string.Empty;

        protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
        {
            RunAsyncCalled = true;
            return Task.FromResult(new ExecutionResult());
        }

        // Expose protected method for testing
        public void TestSetupProperties(StepExecutionContext context)
        {
            SetupProperties(context);
        }
    }
}