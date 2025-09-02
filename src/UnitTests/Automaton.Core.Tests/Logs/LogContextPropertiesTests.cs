using Automaton.Core.Logs;

namespace Automaton.Core.Tests.Logs
{
    [TestFixture]
    public class LogContextPropertiesTests
    {
        [Test]
        public void WorkflowExecutionId_ShouldHaveCorrectValue()
        {
            // Act & Assert
            Assert.That(LogContextProperties.WorkflowExecutionId, Is.EqualTo("WorkflowExecutionId"));
        }

        [Test]
        public void WorkflowId_ShouldHaveCorrectValue()
        {
            // Act & Assert
            Assert.That(LogContextProperties.WorkflowId, Is.EqualTo("WorkflowId"));
        }

        [Test]
        public void WorkflowName_ShouldHaveCorrectValue()
        {
            // Act & Assert
            Assert.That(LogContextProperties.WorkflowName, Is.EqualTo("WorkflowName"));
        }

        [Test]
        public void StepId_ShouldHaveCorrectValue()
        {
            // Act & Assert
            Assert.That(LogContextProperties.StepId, Is.EqualTo("StepId"));
        }

        [Test]
        public void StepName_ShouldHaveCorrectValue()
        {
            // Act & Assert
            Assert.That(LogContextProperties.StepName, Is.EqualTo("StepName"));
        }

        [Test]
        public void AllProperties_ShouldBeStringConstants()
        {
            // Arrange
            var type = typeof(LogContextProperties);
            var fields = type.GetFields();

            // Act & Assert
            foreach (var field in fields)
            {
                Assert.That(field.IsLiteral, Is.True, $"Field {field.Name} should be a constant");
                Assert.That(field.IsStatic, Is.True, $"Field {field.Name} should be static");
                Assert.That(field.FieldType, Is.EqualTo(typeof(string)), $"Field {field.Name} should be of type string");
            }
        }

        [Test]
        public void Properties_ShouldBeUniqueValues()
        {
            // Arrange
            var properties = new[]
            {
                LogContextProperties.WorkflowExecutionId,
                LogContextProperties.WorkflowId,
                LogContextProperties.WorkflowName,
                LogContextProperties.StepId,
                LogContextProperties.StepName
            };

            // Act & Assert
            var uniqueProperties = properties.Distinct().ToArray();
            Assert.That(uniqueProperties.Length, Is.EqualTo(properties.Length), 
                "All log context properties should have unique values");
        }

        [Test]
        public void Properties_ShouldNotBeNullOrEmpty()
        {
            // Act & Assert
            Assert.That(LogContextProperties.WorkflowExecutionId, Is.Not.Null.And.Not.Empty);
            Assert.That(LogContextProperties.WorkflowId, Is.Not.Null.And.Not.Empty);
            Assert.That(LogContextProperties.WorkflowName, Is.Not.Null.And.Not.Empty);
            Assert.That(LogContextProperties.StepId, Is.Not.Null.And.Not.Empty);
            Assert.That(LogContextProperties.StepName, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Properties_ShouldFollowNamingConvention()
        {
            // Arrange & Act
            var properties = new[]
            {
                (nameof(LogContextProperties.WorkflowExecutionId), LogContextProperties.WorkflowExecutionId),
                (nameof(LogContextProperties.WorkflowId), LogContextProperties.WorkflowId),
                (nameof(LogContextProperties.WorkflowName), LogContextProperties.WorkflowName),
                (nameof(LogContextProperties.StepId), LogContextProperties.StepId),
                (nameof(LogContextProperties.StepName), LogContextProperties.StepName)
            };

            // Assert - Property names should match their constant values
            foreach (var (fieldName, fieldValue) in properties)
            {
                Assert.That(fieldValue, Is.EqualTo(fieldName), 
                    $"Property {fieldName} should have value matching its name");
            }
        }

        [Test]
        public void Class_ShouldNotBeInstantiable()
        {
            // Arrange
            var type = typeof(LogContextProperties);

            // Act & Assert
            var constructors = type.GetConstructors();
            Assert.That(constructors.All(c => c.IsPrivate || c.IsFamily), Is.True,
                "LogContextProperties should not have public constructors since it's a constants-only class");
        }
    }
}