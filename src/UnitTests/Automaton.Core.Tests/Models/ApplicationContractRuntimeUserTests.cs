using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Core.Tests.Models;

[TestFixture]
public sealed class ApplicationContractRuntimeUserTests
{
    [TestCase("1000")]
    [TestCase("1000:1000")]
    public void NumericRuntimeUserIsValid(string user)
    {
        var errors = ApplicationContractValidator.Validate(new()
        {
            Runtime = new() { User = user }
        });

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void RuntimeUserSurvivesContractSerialization()
    {
        var contract = new ApplicationContract { Runtime = new() { User = "1000:1000" } };

        var restored = ApplicationContractSerialization.Deserialize(
            ApplicationContractSerialization.Serialize(contract));

        Assert.That(restored.Runtime.User, Is.EqualTo("1000:1000"));
    }

    [TestCase("")]
    [TestCase("1000:")]
    [TestCase(":1000")]
    [TestCase("root")]
    [TestCase("1000:1000:1000")]
    [TestCase("4294967296")]
    [TestCase("1000 --privileged")]
    public void InvalidRuntimeUserIsRejected(string user)
    {
        var errors = ApplicationContractValidator.Validate(new()
        {
            Runtime = new() { User = user }
        });

        Assert.That(errors, Does.Contain("runtime.user must be a numeric UID or UID:GID"));
    }

    [Test]
    public void RequestedAppPermissionSurvivesSerializationAndUnknownPermissionsAreRejected()
    {
        var restored = ApplicationContractSerialization.Deserialize(ApplicationContractSerialization.Serialize(
            new ApplicationContract { Permissions = ["backups:read"] }));
        Assert.That(restored.Permissions, Is.EqualTo(new[] { "backups:read" }));

        var errors = ApplicationContractValidator.Validate(new ApplicationContract { Permissions = ["admin:all"] });
        Assert.That(errors, Has.Some.Contains("unsupported permission"));
        Assert.That(ApplicationSecretNames.IsValidName("APP_AUTOMATON_API_KEY"), Is.False, "the platform delivers the app permission credential under this name");
    }
}
