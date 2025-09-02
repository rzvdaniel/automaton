using Automaton.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Core.Tests.Extensions
{
    [TestFixture]
    public class ServiceCollectionExtensionsTests
    {
        [Test]
        public void AddAutomatonCore_ShouldRegisterFlowExecuteServiceAsTransient()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddAutomatonCore();

            // Assert
            var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(FlowExecuteService));
            
            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Transient));
        }

        [Test]
        public void AddAutomatonCore_ShouldRegisterFlowConvertServiceAsScoped()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddAutomatonCore();

            // Assert
            var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(FlowConvertService));
            
            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
        }

        [Test]
        public void AddAutomatonCore_ShouldReturnServiceCollection()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            var result = services.AddAutomatonCore();

            // Assert
            Assert.That(result, Is.SameAs(services));
        }

        [Test]
        public void AddAutomatonCore_ShouldAllowChaining()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act & Assert - Should not throw and allow method chaining
            Assert.DoesNotThrow(() => 
            {
                services
                    .AddAutomatonCore()
                    .AddSingleton<string>("test");
            });

            Assert.That(services.Count, Is.EqualTo(3)); // 2 from AddAutomatonCore + 1 from AddSingleton
        }

        [Test]
        public void AddAutomatonCore_CalledMultipleTimes_ShouldRegisterServicesMultipleTimes()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddAutomatonCore();
            services.AddAutomatonCore();

            // Assert
            var flowExecuteDescriptors = services.Where(s => s.ServiceType == typeof(FlowExecuteService));
            var flowConvertDescriptors = services.Where(s => s.ServiceType == typeof(FlowConvertService));
            
            Assert.That(flowExecuteDescriptors.Count(), Is.EqualTo(2));
            Assert.That(flowConvertDescriptors.Count(), Is.EqualTo(2));
        }

        [Test]
        public void AddAutomatonCore_WithExistingServices_ShouldAddToCollection()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<string>("existing");

            // Act
            services.AddAutomatonCore();

            // Assert
            Assert.That(services.Count, Is.EqualTo(3)); // 1 existing + 2 from AddAutomatonCore
            
            var stringService = services.FirstOrDefault(s => s.ServiceType == typeof(string));
            Assert.That(stringService, Is.Not.Null);
            Assert.That(stringService.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        }
    }
}