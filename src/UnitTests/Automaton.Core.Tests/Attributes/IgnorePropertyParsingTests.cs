using Automaton.Core.Attributes;

namespace Automaton.Core.Tests.Attributes
{
    [TestFixture]
    public class IgnorePropertyParsingTests
    {
        [Test]
        public void Constructor_WithTrueValue_ShouldSetIgnoreToTrue()
        {
            // Act
            var attribute = new IgnorePropertyParsing(true);

            // Assert
            Assert.That(attribute.Ignore, Is.True);
        }

        [Test]
        public void Constructor_WithFalseValue_ShouldSetIgnoreToFalse()
        {
            // Act
            var attribute = new IgnorePropertyParsing(false);

            // Assert
            Assert.That(attribute.Ignore, Is.False);
        }

        [Test]
        public void AttributeUsage_ShouldBeAppliedToProperty()
        {
            // Arrange
            var type = typeof(TestClassForAttribute);
            var property = type.GetProperty(nameof(TestClassForAttribute.TestProperty));

            // Act
            var attributes = property?.GetCustomAttributes(typeof(IgnorePropertyParsing), false);

            // Assert
            Assert.That(attributes, Is.Not.Null);
            Assert.That(attributes.Length, Is.EqualTo(1));
            var attribute = attributes[0] as IgnorePropertyParsing;
            Assert.That(attribute?.Ignore, Is.True);
        }

        [Test]
        public void AttributeUsage_ShouldInheritFromAttribute()
        {
            // Arrange
            var attribute = new IgnorePropertyParsing(true);

            // Act & Assert
            Assert.That(attribute, Is.InstanceOf<Attribute>());
        }

        [Test]
        public void Property_ShouldBeReadOnly()
        {
            // Arrange
            var attribute = new IgnorePropertyParsing(true);

            // Act & Assert
            var propertyInfo = typeof(IgnorePropertyParsing).GetProperty(nameof(IgnorePropertyParsing.Ignore));
            Assert.That(propertyInfo?.CanWrite, Is.False);
            Assert.That(propertyInfo?.CanRead, Is.True);
        }

        [Test]
        public void MultipleInstances_WithDifferentValues_ShouldRetainIndividualState()
        {
            // Act
            var attributeTrue = new IgnorePropertyParsing(true);
            var attributeFalse = new IgnorePropertyParsing(false);

            // Assert
            Assert.That(attributeTrue.Ignore, Is.True);
            Assert.That(attributeFalse.Ignore, Is.False);
        }
    }

    // Test class for testing attribute usage
    public class TestClassForAttribute
    {
        [IgnorePropertyParsing(true)]
        public string TestProperty { get; set; } = string.Empty;

        public string NormalProperty { get; set; } = string.Empty;
    }
}