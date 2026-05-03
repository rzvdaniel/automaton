using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;
using Fluid;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Resources;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class EmailService(ConfigurationService configurationService, ILogger<EmailService> logger) : IEmailService
{
    private static readonly ResourceManager TemplateResourceManager = new("Automaton.Studio.Server.Resources.EmailTemplates", typeof(EmailService).Assembly);
    private static readonly FluidParser TemplateParser = new();

    public async Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken)
    {
        try
        {
            var (subject, bodyHtml) = await RenderEmailAsync(emailDetails, cancellationToken);
            var message = BuildMessage(emailDetails, subject, bodyHtml);

            using var client = new SmtpClient();
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            
            var socketOptions = ResolveSocketOptions(configurationService.EmailConfig.EmailPort, configurationService.EmailConfig.EmailUseSsl);
            
            await client.ConnectAsync(
                configurationService.EmailConfig.EmailHost,
                configurationService.EmailConfig.EmailPort,
                socketOptions,
                cancellationToken);

            await client.AuthenticateAsync(
                configurationService.EmailConfig.EmailAccount,
                configurationService.EmailConfig.EmailPassword,
                cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            
            logger.LogInformation("Email sent successfully to {ToCount} recipient(s)", emailDetails.To?.Count() ?? 0);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email: {ExceptionMessage}", ex.Message);
            throw;
        }
    }

    private static SecureSocketOptions ResolveSocketOptions(int port, bool useSsl)
    {
        // Port 587 is reserved for STARTTLS (explicit TLS)
        // Port 465 is reserved for implicit SSL/TLS (SMTPS)
        // Port 25 is typically plain text or opportunistic STARTTLS
        if (port == 587)
        {
            return SecureSocketOptions.StartTls;
        }
        
        if (port == 465)
        {
            return SecureSocketOptions.SslOnConnect;
        }
        
        // For other ports, respect the useSsl configuration
        return useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
    }

    private async Task<(string subject, string bodyHtml)> RenderEmailAsync(EmailData emailDetails, CancellationToken cancellationToken)
    {
        var subjectTemplate = GetTemplate(emailDetails.Template, ".Subject")
            ?? GetTemplate(emailDetails.Template, "_Subject")
            ?? (emailDetails.Variables.TryGetValue("Subject", out var subjectFallback) ? subjectFallback : emailDetails.Template);

        var bodyTemplate = GetTemplate(emailDetails.Template, ".Body")
            ?? GetTemplate(emailDetails.Template, "_Body")
            ?? GetTemplate(emailDetails.Template, string.Empty);

        if (string.IsNullOrWhiteSpace(bodyTemplate))
        {
            throw new InvalidOperationException($"Email template '{emailDetails.Template}' was not found in resources.");
        }

        var subject = await RenderTemplateAsync(subjectTemplate, emailDetails.Variables, cancellationToken);
        var bodyHtml = await RenderTemplateAsync(bodyTemplate, emailDetails.Variables, cancellationToken);
        return (subject.Trim(), bodyHtml);
    }

    private static string? GetTemplate(string templateName, string suffix)
    {
        var resourceKey = string.IsNullOrWhiteSpace(suffix)
            ? templateName
            : $"{templateName}{suffix}";

        return TemplateResourceManager.GetString(resourceKey);
    }

    private static async Task<string> RenderTemplateAsync(string template, Dictionary<string, string> variables, CancellationToken cancellationToken)
    {
        if (!TemplateParser.TryParse(template, out var fluidTemplate, out var errors))
        {
            var errorMessage = errors is not null ? string.Join("; ", errors) : "Unknown Liquid parse error.";
            throw new InvalidOperationException($"Failed to parse email template. {errorMessage}");
        }

        var contextVariables = variables.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
        var context = new TemplateContext(contextVariables);
        cancellationToken.ThrowIfCancellationRequested();
        return await fluidTemplate.RenderAsync(context);
    }

    private MimeMessage BuildMessage(EmailData emailDetails, string subject, string bodyHtml)
    {
        var message = new MimeMessage();

        var fromAddress = emailDetails.From?.Email ?? configurationService.EmailConfig.EmailAccount;
        var fromName = emailDetails.From?.Name ?? "Automaton";

        message.From.Add(new MailboxAddress(fromName, fromAddress));

        foreach (var to in emailDetails.To)
        {
            message.To.Add(new MailboxAddress(to.Name, to.Email));
        }

        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = bodyHtml
        };

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }
}
