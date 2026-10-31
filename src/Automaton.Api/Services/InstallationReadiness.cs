using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Automaton.Api.Services;

public sealed record InstallationCheck(string Name, string Status, string Detail);

public static class InstallationReadiness
{
    public static async Task<IReadOnlyList<InstallationCheck>> CheckAsync(
        ApplicationDbContext db, IConfiguration configuration, SmtpSettingsService smtpSettings, CancellationToken token)
    {
        var checks = new List<InstallationCheck>();
        var databaseReady = await db.Database.CanConnectAsync(token);
        checks.Add(new("Database", databaseReady ? "verified" : "error", databaseReady ? "Connected." : "Connection failed."));
        checks.Add(await CheckBrokerAsync(configuration, token));
        var signingReady = !string.IsNullOrWhiteSpace(configuration["AuthenticationSettings:RsaSettings:RsaPrivateKey"])
            || !string.IsNullOrWhiteSpace(configuration["AuthenticationSettings:HMacSettings:SecretKey"]);
        checks.Add(new("Signing keys", signingReady ? "configured" : "error", "Signing credentials are never displayed."));
        var keysPath = configuration["DataProtection:KeysPath"];
        checks.Add(new("Persistent keys", !string.IsNullOrWhiteSpace(keysPath) && Directory.Exists(keysPath) ? "configured" : "error",
            "Include the data-protection key volume in your backups."));
        var apiUrl = configuration["AppConfig:ApiUrl"];
        var studioUrl = configuration["AppConfig:WebsiteUrl"];
        var urlsConfigured = Uri.TryCreate(apiUrl, UriKind.Absolute, out var api) && !string.IsNullOrWhiteSpace(api.Host)
            && Uri.TryCreate(studioUrl, UriKind.Absolute, out var studio) && !string.IsNullOrWhiteSpace(studio.Host);
        checks.Add(new("Public URLs", urlsConfigured ? "configured" : "error",
            urlsConfigured ? "API and Studio addresses are saved. This does not verify DNS or public access."
                : "Configure the API and Studio addresses on the server."));
        checks.Add(new("Public DNS and ports", "unverified",
            "Check api, studio, store, apps and *.apps DNS records from outside this server, and allow inbound TCP 80, 443 and 5671."));
        checks.Add(await CheckSmtpAsync(smtpSettings, token));
        checks.Add(InstallationBackups.ToCheck(await InstallationBackups.ReadAsync(configuration, token), DateTimeOffset.UtcNow));
        return checks;
    }

    private static async Task<InstallationCheck> CheckSmtpAsync(SmtpSettingsService smtpSettings, CancellationToken token)
    {
        try
        {
            var smtp = await smtpSettings.GetSummaryAsync(token);
            var configured = smtp.PasswordSet && !string.IsNullOrWhiteSpace(smtp.EmailHost)
                && smtp.EmailPort > 0 && !string.IsNullOrWhiteSpace(smtp.EmailAccount);
            if (smtp.VerifiedAt.HasValue)
                return new("SMTP", "verified", "SMTP authentication succeeded for these saved settings; delivery has not been tested.");
            if (!configured)
                return new("SMTP", "unverified", "Configure SMTP later for invitations and password recovery.");
            return new("SMTP", "configured", "SMTP settings are present; authentication and delivery have not been tested.");
        }
        catch (SmtpSettingsUnreadableException error)
        {
            return new("SMTP", "error", error.Message);
        }
    }

    private static async Task<InstallationCheck> CheckBrokerAsync(IConfiguration configuration, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMq:Host"] ?? "rabbitmq",
                Port = configuration.GetValue("RabbitMq:Port", 5672),
                UserName = configuration["RabbitMq:Username"] ?? "",
                Password = configuration["RabbitMq:Password"] ?? "",
                VirtualHost = configuration["RabbitMq:VirtualHost"] ?? "/",
                AutomaticRecoveryEnabled = false
            };
            await using var connection = await factory.CreateConnectionAsync(timeout.Token);
            return new("Message broker", "verified", "Authenticated successfully.");
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return new("Message broker", "error", "Connection failed. Check server configuration and container logs.");
        }
    }
}
