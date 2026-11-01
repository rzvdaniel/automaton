using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests;

[TestFixture]
public sealed class SecretsCommandTests
{
    [TestCase("s3cret\n", "s3cret")]
    [TestCase("s3cret\r\n", "s3cret")]
    [TestCase("line one\nline two\n", "line one\nline two")]
    [TestCase("  padded  ", "  padded  ")]
    public void ReadsTheValueFromStandardInputDroppingOnlyOneTrailingLineEnding(string input, string expected)
        => Assert.That(SecretsCommand.ReadValue("KEY", asJson: true, new StringReader(input), redirected: true), Is.EqualTo(expected));

    [Test]
    public void RefusesAnEmptyValueAndAMissingPipeInJsonMode()
    {
        Assert.Throws<InvalidOperationException>(() => SecretsCommand.ReadValue("KEY", asJson: true, new StringReader("\n"), redirected: true));
        Assert.Throws<InvalidOperationException>(() => SecretsCommand.ReadValue("KEY", asJson: true, redirected: false));
    }

    [Test]
    public void NamesOnlyMissingSecretsInTheWarning()
    {
        using var status = JsonDocument.Parse("""
            [{"name":"OWN","present":true,"providedBy":"app"},
             {"name":"STRIPE_KEY","present":false},
             {"name":"SMTP_PASSWORD","present":false}]
            """);
        Assert.That(SecretStatusCheck.Describe(status.RootElement), Does.StartWith("declared secrets without a value: STRIPE_KEY, SMTP_PASSWORD."));
        using var complete = JsonDocument.Parse("""[{"name":"OWN","present":true}]""");
        Assert.That(SecretStatusCheck.Describe(complete.RootElement), Is.Null);
    }
}
