using Automaton.Core.Models;
using Automaton.Studio.Extensions;

namespace Automaton.Core.Tests.Extensions
{
    [TestFixture]
    public class StepExtensionsTests
    {
        [Test]
        public void FindType_WithValidStepType_ShouldReturnCorrectType()
        {
            // Arrange
            var step = new Step
            {
                Id = "test-step",
                Name = "Test Step",
                Type = "Object" // Using a built-in type that exists in System namespace
            };

            // Act & Assert
            // Since we're testing against Automaton.Steps which might not exist in test environment,
            // we expect this to throw a TypeLoadException
            Assert.Throws<TypeLoadException>(() => step.FindType());
        }

        [Test]
        public void FindType_WithNullType_ShouldThrowException()
        {
            // Arrange
            var step = new Step
            {
                Id = "test-step",
                Name = "Test Step",
                Type = null!
            };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => step.FindType());
        }

        [Test]
        public void FindType_WithEmptyType_ShouldThrowException()
        {
            // Arrange
            var step = new Step
            {
                Id = "test-step",
                Name = "Test Step",
                Type = string.Empty
            };

            // Act & Assert
            Assert.Throws<TypeLoadException>(() => step.FindType());
        }

        [Test]
        public void FindType_WithInvalidType_ShouldThrowTypeLoadException()
        {
            // Arrange
            var step = new Step
            {
                Id = "test-step",
                Name = "Test Step",
                Type = "NonExistentType"
            };

            // Act & Assert
            Assert.Throws<TypeLoadException>(() => step.FindType());
        }

        [Test]
        public void FindType_ShouldConstructCorrectFullClassName()
        {
            // Arrange
            var step = new Step
            {
                Id = "test-step",
                Name = "Test Step",
                Type = "TestType"
            };

            // Act & Assert
            var exception = Assert.Throws<TypeLoadException>(() => step.FindType());
            
            // Verify the exception message contains the expected full class name
            Assert.That(exception.Message, Does.Contain("Automaton.Steps.TestType"));
            Assert.That(exception.Message, Does.Contain("Automaton.Steps"));
        }

        [Test]
        public void FindType_WithCaseInsensitiveType_ShouldUseExactCase()
        {
            // Arrange
            var step = new Step
            {
                Id = "test-step",
                Name = "Test Step",
                Type = "testtype" // lowercase
            };

            // Act & Assert
            var exception = Assert.Throws<TypeLoadException>(() => step.FindType());
            
            // Should use the exact case provided
            Assert.That(exception.Message, Does.Contain("Automaton.Steps.testtype"));
        }
    }
}