using Automaton.Core.Models;
using Automaton.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Automaton.Core.Tests.Services
{
    [TestFixture]
    public class FlowConvertServiceTests
    {
        private FlowConvertService _flowConvertService;
        private IServiceProvider _serviceProvider;

        [SetUp]
        public void Setup()
        {
            _serviceProvider = Substitute.For<IServiceProvider>();
            _flowConvertService = new FlowConvertService(_serviceProvider);
        }

        [Test]
        public void ConvertFlow_ShouldCreateWorkflowWithSameBasicProperties()
        {
            // Arrange
            var flow = new Flow
            {
                Id = Guid.NewGuid(),
                Name = "Test Flow",
                StartupDefinitionId = "startup-def"
            };

            var testVariable = new StepVariable { Id = "var1", Name = "Variable1", Value = "test" };
            flow.Variables.Add("var1", testVariable);

            var outputVariable = new StepVariable { Id = "out1", Name = "Output1", Value = "output" };
            flow.OutputVariables.Add("out1", outputVariable);

            // Act
            var workflow = _flowConvertService.ConvertFlow(flow);

            // Assert
            Assert.That(workflow.Id, Is.EqualTo(flow.Id));
            Assert.That(workflow.Name, Is.EqualTo(flow.Name));
            Assert.That(workflow.StartupDefinitionId, Is.EqualTo(flow.StartupDefinitionId));
            Assert.That(workflow.Variables, Is.EqualTo(flow.Variables));
            Assert.That(workflow.OutputVariables, Is.EqualTo(flow.OutputVariables));
        }

        [Test]
        public void ConvertFlow_WithEmptyFlow_ShouldCreateEmptyWorkflow()
        {
            // Arrange
            var flow = new Flow
            {
                Id = Guid.NewGuid(),
                Name = "Empty Flow",
                StartupDefinitionId = "none"
            };

            // Act
            var workflow = _flowConvertService.ConvertFlow(flow);

            // Assert
            Assert.That(workflow.Id, Is.EqualTo(flow.Id));
            Assert.That(workflow.Name, Is.EqualTo(flow.Name));
            Assert.That(workflow.Variables.Count, Is.EqualTo(0));
            Assert.That(workflow.OutputVariables.Count, Is.EqualTo(0));
            Assert.That(workflow.Definitions.Count, Is.EqualTo(0));
        }
    }

    // Test implementation for WorkflowStep since it's abstract
    public class TestWorkflowStepForService : WorkflowStep
    {
        protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
        {
            return Task.FromResult(new ExecutionResult());
        }
    }
}