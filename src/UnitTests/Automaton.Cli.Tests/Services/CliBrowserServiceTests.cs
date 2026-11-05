using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class CliBrowserServiceTests
{
    [Test]
    public void TryOpenUrl_WhenTheUrlCannotBeOpened_ReturnsFalseInsteadOfThrowing()
        => Assert.That(CliBrowserService.TryOpenUrl(""), Is.False);
}
