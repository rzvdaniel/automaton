using Automaton.Studio.Server.Services;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class ApiKeyServiceTests
{
    [Test]
    public void GenerateRawKey_ReturnsFixedLengthLowercaseHex()
    {
        var rawKey = ApiKeyService.GenerateRawKey();

        Assert.That(rawKey, Has.Length.EqualTo(40));
        Assert.That(Regex.IsMatch(rawKey, "^[0-9a-f]{40}$"), Is.True);
    }

    [Test]
    public void GenerateRawKey_RemainsFixedLengthAcrossManyIterations()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var rawKey = ApiKeyService.GenerateRawKey();

            Assert.That(rawKey, Has.Length.EqualTo(40), $"Unexpected key length at iteration {i}.");
            Assert.That(Regex.IsMatch(rawKey, "^[0-9a-f]{40}$"), Is.True, $"Unexpected key format at iteration {i}.");
        }
    }
}
