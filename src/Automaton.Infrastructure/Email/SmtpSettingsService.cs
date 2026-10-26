using System.Security.Cryptography;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Automaton.Api.Services;

public sealed record SmtpSettingsUpdate(string EmailHost, int EmailPort, bool EmailUseSsl,
    string EmailAccount, string? EmailPassword, string FromEmail, string SupportEmail);
public sealed record SmtpTestEmailRequest(string Recipient);

public sealed record SmtpSettingsSummary(string Source, string EmailHost, int EmailPort, bool EmailUseSsl,
    string EmailAccount, string FromEmail, string SupportEmail, bool PasswordSet, DateTimeOffset? VerifiedAt);

public sealed record EffectiveSmtpSettings(EmailConfig Config, bool Managed);

public sealed class SmtpSettingsUnreadableException : Exception
{
    public SmtpSettingsUnreadableException() : base("Saved SMTP settings cannot be decrypted. Restore the matching Data Protection key ring and database backup.") { }
}

public sealed class SmtpSettingsService(ApplicationDbContext db, IOptions<EmailConfig> fileOptions,
    IDataProtectionProvider protection)
{
    private const string SettingKey = "Email.Smtp";
    private readonly IDataProtector protector = protection.CreateProtector("Automaton.Email.SmtpSettings", "v1");

    public async Task<EffectiveSmtpSettings> GetEffectiveAsync(CancellationToken token)
    {
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == SettingKey, token);
        return row is null
            ? new(fileOptions.Value, false)
            : new(ReadDocument(row.Value).Config, true);
    }

    public async Task<SmtpSettingsSummary> GetSummaryAsync(CancellationToken token)
    {
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == SettingKey, token);
        if (row is null)
        {
            var file = fileOptions.Value;
            return Summary(file, string.IsNullOrWhiteSpace(file.EmailHost) ? "none" : "file", null);
        }
        var document = ReadDocument(row.Value);
        return Summary(document.Config, "studio", document.VerifiedAt);
    }

    public async Task SaveAsync(SmtpSettingsUpdate input, CancellationToken token)
    {
        Validate(input);
        var row = await db.Settings.SingleOrDefaultAsync(item => item.Key == SettingKey, token);
        var current = row is null ? null : ReadDocument(row.Value);
        var password = string.IsNullOrEmpty(input.EmailPassword) ? current?.Config.EmailPassword : input.EmailPassword;
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Enter an SMTP password when first saving Studio-managed settings.");
        var config = new EmailConfig
        {
            EmailHost = input.EmailHost.Trim(), EmailPort = input.EmailPort, EmailUseSsl = input.EmailUseSsl,
            EmailAccount = input.EmailAccount.Trim(), EmailPassword = password,
            FromEmail = input.FromEmail.Trim(), SupportEmail = input.SupportEmail.Trim()
        };
        var encrypted = protector.Protect(JsonSerializer.Serialize(new SmtpDocument(config, null)));
        if (row is null)
            db.Settings.Add(new Setting { Key = SettingKey, Value = encrypted, Description = "Encrypted Studio-managed SMTP settings." });
        else
        {
            row.Value = encrypted;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(token);
    }

    public async Task VerifyAsync(CancellationToken token)
    {
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == SettingKey, token)
            ?? throw new InvalidOperationException("Save Studio-managed SMTP settings before checking the connection.");
        var document = ReadDocument(row.Value);
        var unverified = protector.Protect(JsonSerializer.Serialize(document with { VerifiedAt = null }));
        var current = await db.Settings.Where(item => item.Key == SettingKey && item.Value == row.Value)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Value, unverified)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), token);
        if (current != 1)
            throw new InvalidOperationException("SMTP settings changed during verification. Check the saved settings again.");
        using var client = await ConnectAsync(document.Config, token);
        await client.DisconnectAsync(true, token);
        var verified = protector.Protect(JsonSerializer.Serialize(document with { VerifiedAt = DateTimeOffset.UtcNow }));
        var changed = await db.Settings.Where(item => item.Key == SettingKey && item.Value == unverified)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Value, verified)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), token);
        if (changed != 1)
            throw new InvalidOperationException("SMTP settings changed during verification. Check the saved settings again.");
    }

    public async Task SendTestAsync(string recipient, CancellationToken token)
    {
        if (!IsEmailAddress(recipient))
            throw new ArgumentException("Enter a valid test recipient email address.");
        var effective = await GetEffectiveAsync(token);
        if (!effective.Managed)
            throw new InvalidOperationException("Save Studio-managed SMTP settings before sending a test message.");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Automaton", effective.Config.FromEmail ?? effective.Config.EmailAccount));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "Automaton SMTP test";
        message.Body = new TextPart("plain") { Text = "This is a test message from your Automaton installation." };
        using var client = await ConnectAsync(effective.Config, token);
        await client.SendAsync(message, token);
        await client.DisconnectAsync(true, token);
    }

    public static SecureSocketOptions SocketOptions(int port, bool useSsl)
    {
        if (port == 587) return SecureSocketOptions.StartTls;
        if (port == 465 || useSsl) return SecureSocketOptions.SslOnConnect;
        return SecureSocketOptions.None;
    }

    private static async Task<SmtpClient> ConnectAsync(EmailConfig config, CancellationToken token)
    {
        var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(config.EmailHost, config.EmailPort,
                SocketOptions(config.EmailPort, config.EmailUseSsl), token);
            await client.AuthenticateAsync(config.EmailAccount, config.EmailPassword, token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private SmtpDocument ReadDocument(string encrypted)
    {
        try
        {
            var document = JsonSerializer.Deserialize<SmtpDocument>(protector.Unprotect(encrypted));
            if (document?.Config is null || string.IsNullOrWhiteSpace(document.Config.EmailHost)
                || string.IsNullOrWhiteSpace(document.Config.EmailAccount)
                || string.IsNullOrWhiteSpace(document.Config.EmailPassword))
                throw new JsonException("Incomplete SMTP settings document.");
            return document;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            throw new SmtpSettingsUnreadableException();
        }
    }

    private static SmtpSettingsSummary Summary(EmailConfig config, string source, DateTimeOffset? verifiedAt)
        => new(source, config.EmailHost ?? "", config.EmailPort, config.EmailUseSsl,
            config.EmailAccount ?? "", config.FromEmail ?? "", config.SupportEmail ?? "",
            !string.IsNullOrWhiteSpace(config.EmailPassword), verifiedAt);

    private static void Validate(SmtpSettingsUpdate input)
    {
        if (string.IsNullOrWhiteSpace(input.EmailHost) || input.EmailHost.Length > 255
            || Uri.CheckHostName(input.EmailHost.Trim()) == UriHostNameType.Unknown)
            throw new ArgumentException("Enter a valid SMTP host name or IP address.");
        if (input.EmailPort is < 1 or > 65535)
            throw new ArgumentException("Enter a valid SMTP port.");
        if (string.IsNullOrWhiteSpace(input.EmailAccount) || input.EmailAccount.Length > 320)
            throw new ArgumentException("Enter an SMTP account.");
        if (input.EmailPassword?.Length > 4096)
            throw new ArgumentException("SMTP password is too long.");
        if (!IsEmailAddress(input.FromEmail) || !IsEmailAddress(input.SupportEmail))
            throw new ArgumentException("Enter valid sender and support email addresses.");
    }

    private static bool IsEmailAddress(string address)
        => System.Net.Mail.MailAddress.TryCreate(address, out var parsed)
            && string.Equals(parsed.Address, address.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record SmtpDocument(EmailConfig Config, DateTimeOffset? VerifiedAt);
}
