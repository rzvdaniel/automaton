using Automaton.Core.Enums;
using Automaton.Core.Models;
using Newtonsoft.Json.Linq;

namespace Automaton.Core.Tests.Models
{
    [TestFixture]
    public class StepVariableTests
    {
        [Test]
        public void Value_WithDirectValue_ShouldReturnValue()
        {
            // Arrange
            var variable = new StepVariable { Name = "test", Value = "testValue" };

            // Act
            var result = variable.Value;

            // Assert
            Assert.That(result, Is.EqualTo("testValue"));
        }

        [Test]
        public void Value_WithJArrayValue_ShouldStoreAsJsonString()
        {
            // Arrange
            var jsonArray = JArray.FromObject(new[] { "item1", "item2" });
            var variable = new StepVariable { Name = "test", Value = jsonArray.ToString(Newtonsoft.Json.Formatting.None) };

            // Act
            var result = variable.Value;

            // Assert
            Assert.That(result, Is.EqualTo("[\"item1\",\"item2\"]"));
        }

        [Test]
        public void Value_WithJObjectValue_ShouldStoreAsJsonString()
        {
            // Arrange
            var testObject = new { Name = "TestName", Value = 42 };
            var jsonObject = JObject.FromObject(testObject);
            var variable = new StepVariable { Name = "test", Value = jsonObject.ToString(Newtonsoft.Json.Formatting.None) };

            // Act
            var result = variable.Value;

            // Assert
            Assert.That(result, Does.Contain("\"TestName\""));
            Assert.That(result, Does.Contain("42"));
        }
    }
}