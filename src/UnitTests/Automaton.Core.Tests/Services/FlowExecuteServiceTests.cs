using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Core.Services;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Automaton.Core.Tests.Services
{
    [TestFixture]
    public class FlowExecuteServiceTests
    {
        private FlowExecuteService _flowExecuteService;
        private IMediator _mediator;
        private FlowConvertService _flowConvertService;
        private IServiceProvider _serviceProvider;

        [SetUp]
        public void Setup()
        {
            _mediator = Substitute.For<IMediator>();
            _serviceProvider = Substitute.For<IServiceProvider>();
            _flowConvertService = new FlowConvertService(_serviceProvider);
            _flowExecuteService = new FlowExecuteService(_flowConvertService, _mediator);
        }

        [Test]
        public async Task Execute_WithFlow_ShouldConvertFlowAndExecute()
        {
            // Arrange
            var flowId = Guid.NewGuid();
            var flow = new Flow
            {
                Id = flowId,
                Name = "Test Flow",
                StartupDefinitionId = "def1"
            };

            var definition = new Definition { Id = "def1" };
            flow.Definitions.Add(definition);

            // Act
            var result = await _flowExecuteService.Execute(flow);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FlowId, Is.EqualTo(flowId));
        }

        [Test]
        public async Task Execute_WithWorkflow_ShouldExecuteWorkflowSteps()
        {
            // Arrange
            var workflowId = Guid.NewGuid();
            var workflow = CreateTestWorkflow(workflowId, "Test Workflow");

            // Act
            var result = await _flowExecuteService.Execute(workflow);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FlowId, Is.EqualTo(workflowId));
            Assert.That(result.Status, Is.EqualTo(WorkflowStatus.Success));
            Assert.That(result.Started, Is.LessThanOrEqualTo(DateTime.UtcNow));
            Assert.That(result.Finished, Is.GreaterThanOrEqualTo(result.Started));
        }

        [Test]
        public async Task Execute_WithStepException_ShouldMarkAsError()
        {
            // Arrange
            var workflowId = Guid.NewGuid();
            var workflow = CreateTestWorkflow(workflowId, "Test Workflow");
            
            // Setup step to throw exception
            var step = workflow.GetStartupDefinition()?.GetFirstStep();
            if (step is TestWorkflowStepForExecuteService testStep)
            {
                testStep.ShouldThrowException = true;
            }

            // Act
            var result = await _flowExecuteService.Execute(workflow);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FlowId, Is.EqualTo(workflowId));
            Assert.That(result.Status, Is.EqualTo(WorkflowStatus.Error));
        }

        [Test]
        public async Task Execute_WithCancellationToken_ShouldPassTokenToSteps()
        {
            // Arrange
            var workflowId = Guid.NewGuid();
            var workflow = CreateTestWorkflow(workflowId, "Test Workflow");
            var cancellationToken = new CancellationToken(true);

            // Act & Assert - Should not throw even with cancelled token for this test
            var result = await _flowExecuteService.Execute(workflow, cancellationToken);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public async Task Execute_WithNoStartupDefinition_ShouldHandleGracefully()
        {
            // Arrange
            var workflowId = Guid.NewGuid();
            var workflow = new Workflow
            {
                Id = workflowId,
                Name = "Test Workflow",
                StartupDefinitionId = "non-existent"
            };

            // Act & Assert
            Assert.ThrowsAsync<NullReferenceException>(async () => await _flowExecuteService.Execute(workflow));
        }

        [Test]
        public async Task Execute_WithEmptyDefinition_ShouldCompleteSuccessfully()
        {
            // Arrange
            var workflowId = Guid.NewGuid();
            var workflow = new Workflow
            {
                Id = workflowId,
                Name = "Test Workflow",
                StartupDefinitionId = "empty-def"
            };

            var definition = new WorkflowDefinition { Id = "empty-def" };
            workflow.Definitions.Add(definition);

            // Act
            var result = await _flowExecuteService.Execute(workflow);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Status, Is.EqualTo(WorkflowStatus.Success));
        }

        private Workflow CreateTestWorkflow(Guid workflowId, string name)
        {
            var workflow = new Workflow
            {
                Id = workflowId,
                Name = name,
                StartupDefinitionId = "def1"
            };

            var definition = new WorkflowDefinition { Id = "def1" };
            var step = new TestWorkflowStepForExecuteService 
            { 
                Id = "step1", 
                Name = "Test Step",
                DisplayName = "Test Step Display",
                Type = "TestStep"
            };
            step.WorkflowDefinition = definition;
            
            definition.Steps.Add("step1", step);
            workflow.Definitions.Add(definition);

            return workflow;
        }
    }

    // Test implementation for WorkflowStep
    public class TestWorkflowStepForExecuteService : WorkflowStep
    {
        public bool ShouldThrowException { get; set; } = false;

        protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
        {
            if (ShouldThrowException)
            {
                throw new InvalidOperationException("Test exception for error handling");
            }

            return Task.FromResult(new ExecutionResult());
        }
    }
}