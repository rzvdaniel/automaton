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
        [Description("Concatenate numeric variables - should perform mathematical addition")]
        public void ConcatenateNumberVariables_ShouldPerformMathematicalAddition()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = 1m, Type = VariableType.Number });
            workflow.Variables.Add("B", new StepVariable { Id = "B", Name = "B", Value = 2m, Type = VariableType.Number });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A% + %B%", Type = VariableType.Number };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo(3));
        }

        [Test]
        [Description("Concatenate numeric variables with numbers")]
        public void ConcatenateNumberVariablesWithNumbers_ShouldPerformMathematicalAddition()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = 1m, Type = VariableType.Number });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A% + 1", Type = VariableType.Number };
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo(2));
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
        [Description("Parse numeric expression with subtraction")]
        public void Parse_NumericExpressionWithSubtraction_ShouldCalculateCorrectly()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = 10m, Type = VariableType.Number });
            workflow.Variables.Add("B", new StepVariable { Id = "B", Name = "B", Value = 3m, Type = VariableType.Number });

            var numericVariable = new StepVariable { Id = "C", Name = "C", Value = "%A% - %B%", Type = VariableType.Number };
            var result = stepVariableParser.Parse(numericVariable, workflow);

            Assert.That(result, Is.EqualTo(7));
        }

        [Test]
        [Description("Parse numeric expression with multiplication")]
        public void Parse_NumericExpressionWithMultiplication_ShouldCalculateCorrectly()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = 4m, Type = VariableType.Number });
            workflow.Variables.Add("B", new StepVariable { Id = "B", Name = "B", Value = 5m, Type = VariableType.Number });

            var numericVariable = new StepVariable { Id = "C", Name = "C", Value = "%A% * %B%", Type = VariableType.Number };
            var result = stepVariableParser.Parse(numericVariable, workflow);

            Assert.That(result, Is.EqualTo(20));
        }

        [Test]
        [Description("Parse numeric expression with division")]
        public void Parse_NumericExpressionWithDivision_ShouldCalculateCorrectly()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = 10m, Type = VariableType.Number });
            workflow.Variables.Add("B", new StepVariable { Id = "B", Name = "B", Value = 2m, Type = VariableType.Number });

            var numericVariable = new StepVariable { Id = "C", Name = "C", Value = "%A% / %B%", Type = VariableType.Number };
            var result = stepVariableParser.Parse(numericVariable, workflow);

            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        [Description("Parse with StepVariable as input value")]
        public void Parse_WithStepVariableAsValue_ShouldParseCorrectly()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = "test", Type = VariableType.String });

            var innerVariable = new StepVariable { Id = "Inner", Name = "Inner", Value = "%A%", Type = VariableType.String };
            var outerVariable = new StepVariable { Id = "Outer", Name = "Outer", Value = innerVariable, Type = VariableType.String };
            
            var result = stepVariableParser.Parse(outerVariable, workflow);

            Assert.That(result, Is.EqualTo("test"));
        }

        [Test]
        public void Parse_BooleanVariableType_ShouldReturnBooleanValue()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = true, Type = VariableType.Boolean });

            var boolVariable = new StepVariable { Id = "B", Name = "B", Value = "%A%", Type = VariableType.Boolean };
            var result = stepVariableParser.Parse(boolVariable, workflow);

            // Boolean variables that don't match specific types fall back to string handling
            Assert.That(result, Is.EqualTo(true));
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
        [Description("Parse complex expression with multiple variable types")]
        public void Parse_ComplexExpression_ShouldHandleCorrectly()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("Name", new StepVariable { Id = "Name", Name = "Name", Value = "John", Type = VariableType.String });
            workflow.Variables.Add("Age", new StepVariable { Id = "Age", Name = "Age", Value = 25m, Type = VariableType.Number });

            var stringVariable = new StepVariable 
            { 
                Id = "Result", 
                Name = "Result", 
                Value = "User %Name% is %Age% years old", 
                Type = VariableType.String 
            };
            
            var result = stepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("User John is 25 years old"));
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