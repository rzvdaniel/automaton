using Automaton.Cli.Commands;
using NUnit.Framework;

namespace Automaton.Cli.Tests;

[TestFixture]
public sealed class KeysAndPasswordLoginTests
{
    [TestCase("developer", 7)]
    [TestCase("Runner", 3)]
    public void ParsesTheScope(string scope, int expected) => Assert.That(KeysCommand.ParseScope(scope), Is.EqualTo(expected));

    [Test]
    public void RefusesScopesAnAgentShouldNotMint() => Assert.Throws<InvalidOperationException>(() => KeysCommand.ParseScope("admin"));

    [Test]
    public void PasswordLoginNeedsAPipeAndANonEmptyPassword()
    {
        Assert.ThatAsync(() => LoginCommand.CreateKeyWithPasswordAsync("http://x", "a@b.c", new StringReader("pw"), redirected: false),
            Throws.InvalidOperationException);
        Assert.ThatAsync(() => LoginCommand.CreateKeyWithPasswordAsync("http://x", "a@b.c", new StringReader("\n"), redirected: true),
            Throws.InvalidOperationException);
    }

    [Test]
    public void KeyFileIsOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            KeysCommand.WritePrivateFile(path, "k");
            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.Throws<IOException>(() => KeysCommand.WritePrivateFile(path, "again"));
        }
        finally { File.Delete(path); }
    }
}
