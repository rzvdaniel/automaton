using Automaton.Core.Enums;
using Automaton.Core.Models;
using NSubstitute;
using Serilog;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class StepVariableParserTests
    {
        private StepVariableParser stepVariableParser;
        private ILogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockLogger = Substitute.For<ILogger>();
            stepVariableParser = new StepVariableParser(mockLogger);
        }

        [Test]
        [Description("Concatenate string variables - should replace variables in string")]
        public void ConcatenateStringVariables_ShouldReplaceVariables()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = "a", Type = VariableType.String });
            workflow.Variables.Add("B", new StepVariable { Id = "B", Name = "B", Value = "b", Type = VariableType.String });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A%%B%", Type = VariableType.String };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("ab"));
        }

        [Test]
        [Description("Concatenate string variables and strings - should replace variables and keep literal text")]
        public void ConcatenateStringVariablesAndStrings_ShouldReplaceVariablesAndKeepLiterals()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = "a", Type = VariableType.String });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A%bc", Type = VariableType.String };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("abc"));
        }

        [Test]
        [Description("Parse expression with no variables should return original value")]
        public void Parse_WithNoVariables_ShouldReturnOriginalValue()
        {
            var workflow = new Workflow();
            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "plain text", Type = VariableType.String };
            
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("plain text"));
        }

        [Test]
        [Description("Parse should throw ArgumentException when value is null")]
        public void Parse_WithNullValue_ShouldThrowArgumentException()
        {
            var workflow = new Workflow();
            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = null, Type = VariableType.String };

            Assert.Throws<ArgumentException>(() => stepVariableParser.Parse(stringVariable, workflow));
        }

        [Test]
        [Description("Parse string with multiple variables")]
        public void Parse_StringWithMultipleVariables_ShouldReplaceAll()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("First", new StepVariable { Id = "First", Name = "First", Value = "Hello", Type = VariableType.String });
            workflow.Variables.Add("Second", new StepVariable { Id = "Second", Name = "Second", Value = "World", Type = VariableType.String });

            var stringVariable = new StepVariable { Id = "Result", Name = "Result", Value = "%First% %Second%!", Type = VariableType.String };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("Hello World!"));
        }

        [Test]
        [Description("Parse with StepVariable as input value")]
        public void Parse_WithStepVariableAsValue_ShouldParseCorrectly()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = "test", Type = VariableType.String });

            var innerVariable = new StepVariable { Id = "Inner", Name = "Inner", Value = "%A%", Type = VariableType.String };
            var outerVariable = new StepVariable { Id = "Outer", Name = "Outer", Value = innerVariable.Value, Type = VariableType.String };
            
            var result = stepVariableParser.Parse(outerVariable, workflow);

            Assert.That(result, Is.EqualTo("test"));
        }

        [Test]
        [Description("Parse with non-existent variable should log error and continue")]
        public void Parse_WithNonExistentVariable_ShouldLogErrorAndContinue()
        {
            var workflow = new Workflow();

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "Hello %NonExistent% World", Type = VariableType.String };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            // Should continue processing and return string with unresolved variable
            Assert.That(result, Is.EqualTo("Hello %NonExistent% World"));
            
            // Verify that error was logged
            mockLogger.Received(1).Error("Variable {0} does not exist", "NonExistent");
        }

        [Test]
        [Description("Parse should handle workflow with null variables gracefully")]
        public void Parse_WithNullWorkflowVariable_ShouldLogErrorAndContinue()
        {
            var workflow = new Workflow();
            // Don't add the variable to simulate null return from GetVariableByName

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "Test %Missing% value", Type = VariableType.String };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("Test %Missing% value"));
            mockLogger.Received(1).Error("Variable {0} does not exist", "Missing");
        }

        [Test]
        [Description("Test backward compatibility with parameterless constructor")]
        public void Constructor_Parameterless_ShouldWorkCorrectly()
        {
            // This test verifies that existing code using the parameterless constructor still works
            var parser = new StepVariableParser();
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = "test", Type = VariableType.String });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A%", Type = VariableType.String };
            var result = parser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("test"));
        }

        [Test]
        [Description("Constructor with null logger should throw ArgumentNullException")]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new StepVariableParser(null!));
        }

        [Test]
        [Description("Parse with empty variable name should handle gracefully")]
        public void Parse_WithEmptyVariableName_ShouldHandleGracefully()
        {
            var workflow = new Workflow();

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "Test %% empty", Type = VariableType.String };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            // Should handle empty variable name gracefully
            Assert.That(result, Is.EqualTo("Test %% empty"));
        }
    }
}