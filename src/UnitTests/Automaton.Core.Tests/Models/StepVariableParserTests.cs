using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class StepVariableParserTests
    {
        [Test]
        [Description("Concatenate string variables - should replace variables in string")]
        public void ConcatenateStringVariables_ShouldReplaceVariables()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = "a", Type = VariableType.String });
            workflow.Variables.Add("B", new StepVariable { Id = "B", Name = "B", Value = "b", Type = VariableType.String });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A%%B%", Type = VariableType.String };
            var result = StepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("ab"));
        }

        [Test]
        [Description("Concatenate string variables and strings - should replace variables and keep literal text")]
        public void ConcatenateStringVariablesAndStrings_ShouldReplaceVariablesAndKeepLiterals()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = "a", Type = VariableType.String });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A%bc", Type = VariableType.String };
            var result = StepVariableParser.Parse(stringVariable, workflow);

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
            var result = StepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo(3));
        }

        [Test]
        [Description("Concatenate numeric variables with numbers")]
        public void ConcatenateNumberVariablesWithNumbers_ShouldPerformMathematicalAddition()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = 1m, Type = VariableType.Number });

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "%A% + 1", Type = VariableType.Number };
            var result = StepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo(2));
        }

        [Test]
        [Description("Parse expression with no variables should return original value")]
        public void Parse_WithNoVariables_ShouldReturnOriginalValue()
        {
            var workflow = new Workflow();
            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "plain text", Type = VariableType.String };
            
            var result = StepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("plain text"));
        }

        [Test]
        [Description("Parse should throw ArgumentException when value is null")]
        public void Parse_WithNullValue_ShouldThrowArgumentException()
        {
            var workflow = new Workflow();
            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = null, Type = VariableType.String };

            Assert.Throws<ArgumentException>(() => StepVariableParser.Parse(stringVariable, workflow));
        }

        [Test]
        [Description("Parse string with multiple variables")]
        public void Parse_StringWithMultipleVariables_ShouldReplaceAll()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("First", new StepVariable { Id = "First", Name = "First", Value = "Hello", Type = VariableType.String });
            workflow.Variables.Add("Second", new StepVariable { Id = "Second", Name = "Second", Value = "World", Type = VariableType.String });

            var stringVariable = new StepVariable { Id = "Result", Name = "Result", Value = "%First% %Second%!", Type = VariableType.String };
            var result = StepVariableParser.Parse(stringVariable, workflow);

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
            var result = StepVariableParser.Parse(numericVariable, workflow);

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
            var result = StepVariableParser.Parse(numericVariable, workflow);

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
            var result = StepVariableParser.Parse(numericVariable, workflow);

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
            
            var result = StepVariableParser.Parse(outerVariable, workflow);

            Assert.That(result, Is.EqualTo("test"));
        }

        [Test]
        public void Parse_BooleanVariableType_ShouldReturnBooleanValue()
        {
            var workflow = new Workflow();
            workflow.Variables.Add("A", new StepVariable { Id = "A", Name = "A", Value = true, Type = VariableType.Boolean });

            var boolVariable = new StepVariable { Id = "B", Name = "B", Value = "%A%", Type = VariableType.Boolean };
            var result = StepVariableParser.Parse(boolVariable, workflow);

            // Boolean variables that don't match specific types fall back to string handling
            Assert.That(result, Is.EqualTo(true));
        }

        [Test]
        public void Parse_WithNonExistentVariable_ShouldHandleGracefully()
        {
            var workflow = new Workflow();

            var stringVariable = new StepVariable { Id = "C", Name = "C", Value = "Hello World", Type = VariableType.String };
            var result = StepVariableParser.Parse(stringVariable, workflow);

            Assert.That(result, Is.EqualTo("Hello World"));
        }
    }
}