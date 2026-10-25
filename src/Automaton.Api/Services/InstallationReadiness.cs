using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Automaton.Api.Services;

public sealed record InstallationCheck(string Name, string Status, string Detail);

public static class InstallationReadiness
{
    public static async Task<IReadOnlyList<InstallationCheck>> CheckAsync(
        ApplicationDbContext db, IConfiguration configuration, CancellationToken token)
    {
        var checks = new List<InstallationCheck>();
        var databaseReady = await db.Database.CanConnectAsync(token);
        checks.Add(new("Database", databaseReady ? "ready" : "error", databaseReady ? "Connected." : "Connection failed."));
        checks.Add(await CheckBrokerAsync(configuration, token));
        var signingReady = !string.IsNullOrWhiteSpace(configuration["AuthenticationSettings:RsaSettings:RsaPrivateKey"])
            || !string.IsNullOrWhiteSpace(configuration["AuthenticationSettings:HMacSettings:SecretKey"]);
        checks.Add(new("Signing keys", signingReady ? "configured" : "error", "Signing credentials are never displayed."));
        var keysPath = configuration["DataProtection:KeysPath"];
        checks.Add(new("Persistent keys", !string.IsNullOrWhiteSpace(keysPath) && Directory.Exists(keysPath) ? "configured" : "error",
            "Include the data-protection key volume in your backups."));
        var emailConfigured = !string.IsNullOrWhiteSpace(configuration["EmailConfig:EmailHost"]);
        checks.Add(new("Email", emailConfigured ? "configured" : "optional",
            emailConfigured ? "SMTP settings are present; delivery has not been tested." : "Configure SMTP later for invitations and password recovery."));
        return checks;
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
            return new("Message broker", "ready", "Authenticated successfully.");
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return new("Message broker", "error", "Connection failed. Check server configuration and container logs.");
        }
    }
}
