using Automaton.Api.Config;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.UnitTests.Shared;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class EmailCertificateValidationTests
{
    [Test]
    public async Task Send_RejectsUntrustedSmtpServerCertificate()
    {
        await using var server = new LoopbackTlsServer();
        await using var db = InstallationTestFixture.Database();
        var service = new EmailService(
            SmtpTestFixture.Service(db, file: new EmailConfig
                {
                    EmailHost = "localhost",
                    EmailPort = server.Port,
                    EmailUseSsl = true,
                    EmailAccount = "sender@example.test",
                    EmailPassword = "test-only"
                }),
            NullLogger<EmailService>.Instance);
        var message = new EmailData
        {
            Template = "AutomatonConfirmEmail",
            From = new EmailAccount { Email = "sender@example.test" },
            To = [new EmailAccount { Email = "recipient@example.test" }]
        };

        Assert.ThrowsAsync<SslHandshakeException>(
            () => service.Send(message, CancellationToken.None));
        Assert.That(server.AcceptedConnection, Is.True);
    }
}
