using Automaton.Core.Models;

namespace Automaton.Core.Tests.Models;

[TestFixture]
public sealed class ApplicationContractExtensionDataTests
{
    [Test]
    public void DeserializeThenSerialize_KeepsUnknownFieldsAndStillValidates()
    {
        var contract = ApplicationContractSerialization.Deserialize(
            """{ "futureField": 1, "health": { "futureHealth": "x" }, "secrets": [ { "name": "API_KEY", "futureSecret": true } ] }""");

        var json = ApplicationContractSerialization.Serialize(contract);

        Assert.That(json, Does.Contain("futureField").And.Contain("futureHealth").And.Contain("futureSecret"));
        Assert.That(contract.Secrets[0].Name, Is.EqualTo("API_KEY"));
    }
}
