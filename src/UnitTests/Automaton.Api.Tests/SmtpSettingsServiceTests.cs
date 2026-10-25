using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class SmtpSettingsServiceTests
{
    [Test]
    public async Task StudioSettingsAreEncryptedAndOverrideFileWithoutReturningPassword()
    {
        await using var db = Database();
        var service = SmtpTestFixture.Service(db);
        Assert.That((await service.GetSummaryAsync(CancellationToken.None)).Source, Is.EqualTo("file"));

        await service.SaveAsync(Update("new-password"), CancellationToken.None);

        await AssertStoredPasswordProtectedAsync(db, "new-password");
        var summary = await service.GetSummaryAsync(CancellationToken.None);
        var effective = await service.GetEffectiveAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(summary.Source, Is.EqualTo("studio"));
            Assert.That(summary.PasswordSet, Is.True);
            Assert.That(summary.VerifiedAt, Is.Null);
            Assert.That(effective.Managed, Is.True);
            Assert.That(effective.Config.EmailPassword, Is.EqualTo("new-password"));
            Assert.That(effective.Config.SupportEmail, Is.EqualTo("support@example.test"));
        });

        await service.SaveAsync(Update(null), CancellationToken.None);
        Assert.That((await service.GetEffectiveAsync(CancellationToken.None)).Config.EmailPassword, Is.EqualTo("new-password"));
    }

    [Test]
    public async Task SavingChangesClearsPreviousVerification()
    {
        await using var db = Database();
        var provider = new EphemeralDataProtectionProvider();
        var service = SmtpTestFixture.Service(db, provider);
        await service.SaveAsync(Update("new-password"), CancellationToken.None);
        await SeedVerifiedAsync(db, provider, service);
        Assert.That((await service.GetSummaryAsync(CancellationToken.None)).VerifiedAt, Is.Not.Null);

        await service.SaveAsync(Update(null) with { FromEmail = "other@example.test" }, CancellationToken.None);

        Assert.That((await service.GetSummaryAsync(CancellationToken.None)).VerifiedAt, Is.Null);
    }

    [Test]
    public async Task MissingKeyRingFailsInsteadOfUsingFileFallback()
    {
        await using var db = Database();
        await SmtpTestFixture.Service(db).SaveAsync(Update("new-password"), CancellationToken.None);
        var anotherKeyRing = SmtpTestFixture.Service(db);

        Assert.ThrowsAsync<SmtpSettingsUnreadableException>(
            () => anotherKeyRing.GetEffectiveAsync(CancellationToken.None));
        Assert.ThrowsAsync<SmtpSettingsUnreadableException>(
            () => anotherKeyRing.GetSummaryAsync(CancellationToken.None));
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task AssertStoredPasswordProtectedAsync(ApplicationDbContext db, string password)
    {
        var row = await db.Settings.SingleAsync(item => item.Key == "Email.Smtp");
        Assert.That(row.Value, Does.Not.Contain(password));
    }

    private static async Task SeedVerifiedAsync(ApplicationDbContext db, IDataProtectionProvider provider,
        SmtpSettingsService service)
    {
        var row = await db.Settings.SingleAsync(item => item.Key == "Email.Smtp");
        var document = new { Config = (await service.GetEffectiveAsync(CancellationToken.None)).Config,
            VerifiedAt = (DateTimeOffset?)DateTimeOffset.UtcNow };
        row.Value = provider.CreateProtector("Automaton.Email.SmtpSettings", "v1")
            .Protect(JsonSerializer.Serialize(document));
        await db.SaveChangesAsync();
    }

    private static SmtpSettingsUpdate Update(string? password) => new("smtp.example.test", 587, true,
        "account@example.test", password, "sender@example.test", "support@example.test");
}

internal static class SmtpTestFixture
{
    internal static SmtpSettingsService Service(ApplicationDbContext db, IDataProtectionProvider? provider = null,
        EmailConfig? file = null)
        => new(db, Options.Create(file ?? new EmailConfig { EmailHost = "file.example.test", EmailPort = 587,
            EmailAccount = "file-account", EmailPassword = "file-password", FromEmail = "file@example.test",
            SupportEmail = "file-support@example.test" }), provider ?? new EphemeralDataProtectionProvider());
}
