using Automaton.Core.Models;
using Automaton.Core.Enums;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class FlowTests
    {
        [Test]
        public void Constructor_ShouldInitializeCollections()
        {
            // Arrange & Act
            var flow = new Flow();

            // Assert
            Assert.That(flow.Variables, Is.Not.Null);
            Assert.That(flow.InputVariables, Is.Not.Null);
            Assert.That(flow.OutputVariables, Is.Not.Null);
            Assert.That(flow.Definitions, Is.Not.Null);
            Assert.That(flow.Variables.Count, Is.EqualTo(0));
            Assert.That(flow.InputVariables.Count, Is.EqualTo(0));
            Assert.That(flow.OutputVariables.Count, Is.EqualTo(0));
            Assert.That(flow.Definitions.Count, Is.EqualTo(0));
        }

        [Test]
        public void Properties_ShouldBeSettableAndGettable()
        {
            // Arrange
            var flowId = Guid.NewGuid();
            var created = DateTime.Now;
            var updated = DateTime.Now.AddHours(1);

            // Act
            var flow = new Flow
            {
                Id = flowId,
                Name = "Test Flow",
                StartupDefinitionId = "startup-def",
                Created = created,
                Updated = updated
            };

            // Assert
            Assert.That(flow.Id, Is.EqualTo(flowId));
            Assert.That(flow.Name, Is.EqualTo("Test Flow"));
            Assert.That(flow.StartupDefinitionId, Is.EqualTo("startup-def"));
            Assert.That(flow.Created, Is.EqualTo(created));
            Assert.That(flow.Updated, Is.EqualTo(updated));
        }

        [Test]
        public void Variables_ShouldAllowAddingStepVariables()
        {
            // Arrange
            var flow = new Flow();
            var variable = new StepVariable { Id = "var1", Name = "Variable1", Value = "test" };

            // Act
            flow.Variables.Add("var1", variable);

            // Assert
            Assert.That(flow.Variables.Count, Is.EqualTo(1));
            Assert.That(flow.Variables["var1"], Is.EqualTo(variable));
        }

        [Test]
        public void Definitions_ShouldAllowAddingDefinitions()
        {
            // Arrange
            var flow = new Flow();
            var definition = new Definition { Id = "def1", Name = "Definition 1" };

            // Act
            flow.Definitions.Add(definition);

            // Assert
            Assert.That(flow.Definitions.Count, Is.EqualTo(1));
            Assert.That(flow.Definitions[0], Is.EqualTo(definition));
        }
    }

    [TestFixture]
    public class DefinitionTests
    {
        [Test]
        public void Constructor_ShouldSetDefaultValues()
        {
            // Arrange & Act
            var definition = new Definition();

            // Assert
            Assert.That(definition.Id, Is.Not.Null);
            Assert.That(definition.Id, Is.Not.Empty);
            Assert.That(definition.Name, Is.EqualTo("Untitled"));
            Assert.That(definition.Steps, Is.Not.Null);
            Assert.That(definition.Steps.Count, Is.EqualTo(0));
        }

        [Test]
        public void Properties_ShouldBeSettableAndGettable()
        {
            // Arrange & Act
            var definition = new Definition
            {
                Id = "custom-id",
                Name = "Custom Definition"
            };

            // Assert
            Assert.That(definition.Id, Is.EqualTo("custom-id"));
            Assert.That(definition.Name, Is.EqualTo("Custom Definition"));
        }

        [Test]
        public void Steps_ShouldAllowAddingSteps()
        {
            // Arrange
            var definition = new Definition();
            var step = new Step { Id = "step1", Name = "Step 1", Type = "TestType" };

            // Act
            definition.Steps.Add(step);

            // Assert
            Assert.That(definition.Steps.Count, Is.EqualTo(1));
            Assert.That(definition.Steps[0], Is.EqualTo(step));
        }
    }

    [TestFixture]
    public class ExecutionResultTests
    {
        [Test]
        public void Constructor_ShouldSetDefaultValues()
        {
            // Arrange & Act
            var result = new ExecutionResult();

            // Assert
            Assert.That(result.Proceed, Is.False); // Default is false
        }

        [Test]
        public void Properties_ShouldBeSettableAndGettable()
        {
            // Arrange & Act
            var result = new ExecutionResult
            {
                Proceed = true
            };

            // Assert
            Assert.That(result.Proceed, Is.True);
        }

        [Test]
        public void Next_ShouldReturnResultWithProceedTrue()
        {
            // Arrange & Act
            var result = ExecutionResult.Next();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Proceed, Is.True);
        }

        [Test]
        public void Stop_ShouldReturnResultWithProceedFalse()
        {
            // Arrange & Act
            var result = ExecutionResult.Stop();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Proceed, Is.False);
        }
    }

    [TestFixture]
    public class ExecutionErrorTests
    {
        [Test]
        public void Properties_ShouldBeSettableAndGettable()
        {
            // Arrange
            var workflowId = "test-workflow-id";
            var errorTime = DateTime.Now;

            // Act
            var error = new ExecutionError
            {
                WorkflowId = workflowId,
                ErrorTime = errorTime,
                Message = "Test error message"
            };

            // Assert
            Assert.That(error.WorkflowId, Is.EqualTo(workflowId));
            Assert.That(error.ErrorTime, Is.EqualTo(errorTime));
            Assert.That(error.Message, Is.EqualTo("Test error message"));
        }
    }

    [TestFixture]
    public class StepExecutionContextTests
    {
        [Test]
        public void Properties_ShouldBeSettableAndGettable()
        {
            // Arrange
            var workflow = new Workflow();
            var definition = new WorkflowDefinition();
            var step = new TestWorkflowStepForModels();
            var cancellationToken = new CancellationToken();

            // Act
            var context = new StepExecutionContext
            {
                Workflow = workflow,
                Definition = definition,
                Step = step,
                CancellationToken = cancellationToken
            };

            // Assert
            Assert.That(context.Workflow, Is.EqualTo(workflow));
            Assert.That(context.Definition, Is.EqualTo(definition));
            Assert.That(context.Step, Is.EqualTo(step));
            Assert.That(context.CancellationToken, Is.EqualTo(cancellationToken));
        }

        [Test]
        public void Constructor_ShouldAllowSettingAllRequiredProperties()
        {
            // Arrange
            var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test Workflow" };
            var definition = new WorkflowDefinition { Id = "test-def" };
            var step = new TestWorkflowStepForModels 
            { 
                Id = "test-step", 
                Name = "Test Step",
                DisplayName = "Test Step Display",
                Type = "TestStep"
            };
            var cancellationToken = CancellationToken.None;

            // Act
            var context = new StepExecutionContext
            {
                Workflow = workflow,
                Definition = definition,
                Step = step,
                CancellationToken = cancellationToken
            };

            // Assert
            Assert.That(context.Workflow, Is.EqualTo(workflow));
            Assert.That(context.Definition, Is.EqualTo(definition));
            Assert.That(context.Step, Is.EqualTo(step));
            Assert.That(context.CancellationToken, Is.EqualTo(cancellationToken));
        }

        [Test]
        public void CancellationToken_ShouldSupportCancelledToken()
        {
            // Arrange
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            // Act
            var context = new StepExecutionContext
            {
                Workflow = new Workflow(),
                Definition = new WorkflowDefinition(),
                Step = new TestWorkflowStepForModels(),
                CancellationToken = cancellationTokenSource.Token
            };

            // Assert
            Assert.That(context.CancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Context_WithComplexWorkflow_ShouldMaintainReferences()
        {
            // Arrange
            var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Complex Workflow" };
            workflow.Variables.Add("var1", new StepVariable { Name = "var1", Value = "value1" });
            
            var definition = new WorkflowDefinition { Id = "complex-def" };
            var step = new TestWorkflowStepForModels { Id = "complex-step" };
            definition.Steps.Add("complex-step", step);
            workflow.Definitions.Add(definition);

            // Act
            var context = new StepExecutionContext
            {
                Workflow = workflow,
                Definition = definition,
                Step = step,
                CancellationToken = CancellationToken.None
            };

            // Assert
            Assert.That(context.Workflow.Variables.Count, Is.EqualTo(1));
            Assert.That(context.Workflow.Definitions.Count, Is.EqualTo(1));
            Assert.That(context.Definition.Steps.Count, Is.EqualTo(1));
            Assert.That(context.Step.Id, Is.EqualTo("complex-step"));
        }
    }

    // Test helper class
    public class TestWorkflowStepForModels : WorkflowStep
    {
        protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
        {
            return Task.FromResult(new ExecutionResult());
        }
    }
}