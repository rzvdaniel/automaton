using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Fluid;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.Resources;

namespace Automaton.Api.Services;

public class EmailService(SmtpSettingsService smtpSettings, ILogger<EmailService> logger) : IEmailService
{
    private static readonly ResourceManager TemplateResourceManager = new("Automaton.Infrastructure.Resources.EmailTemplates", typeof(EmailService).Assembly);
    private static readonly FluidParser TemplateParser = new();

    public async Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken)
    {
        try
        {
            var effective = await smtpSettings.GetEffectiveAsync(cancellationToken);
            var config = effective.Config;
            if (effective.Managed)
                emailDetails.Variables["SupportEmail"] = config.SupportEmail;
            var (subject, bodyHtml) = await RenderEmailAsync(emailDetails, cancellationToken);
            var message = BuildMessage(emailDetails, subject, bodyHtml, config, effective.Managed);

            using var client = new SmtpClient();

            var socketOptions = SmtpSettingsService.SocketOptions(config.EmailPort, config.EmailUseSsl);

            await client.ConnectAsync(
                config.EmailHost,
                config.EmailPort,
                socketOptions,
                cancellationToken);

            await client.AuthenticateAsync(
                config.EmailAccount,
                config.EmailPassword,
                cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogDebug("Email sent successfully to {ToCount} recipient(s)", emailDetails.To?.Count() ?? 0);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email: {ExceptionMessage}", ex.Message);
            throw;
        }
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

    private static MimeMessage BuildMessage(EmailData emailDetails, string subject, string bodyHtml,
        Automaton.Api.Config.EmailConfig config, bool managed)
    {
        var message = new MimeMessage();

        var fromAddress = managed
            ? config.FromEmail ?? config.EmailAccount
            : emailDetails.From?.Email ?? config.FromEmail ?? config.EmailAccount;
        var fromName = emailDetails.From?.Name ?? "Automaton";

        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException(
                "Email sender address is not configured. Set EmailConfig:FromEmail or EmailConfig:EmailAccount for this host.");
        }

        message.From.Add(new MailboxAddress(fromName, fromAddress));

        foreach (var to in emailDetails.To)
        {
            if (string.IsNullOrWhiteSpace(to.Email))
            {
                throw new InvalidOperationException(
                    $"Email recipient address is missing for template '{emailDetails.Template}'.");
            }

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
