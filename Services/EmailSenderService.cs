using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using socconvertor.Models.Email;

namespace socconvertor.Services;

/// <summary>
/// Email sender implementation using MailKit for SMTP
/// </summary>
public class EmailSenderService : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailSenderService> _logger;

    public EmailSenderService(IConfiguration configuration, ILogger<EmailSenderService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var options = GetDefaultEmailOptions();
        await SendEmailWithAttachmentsAsync(to, subject, body, new List<EmailAttachment>(), options);
    }

    public async Task SendEmailWithAttachmentsAsync(
        string to,
        string subject,
        string htmlBody,
        List<EmailAttachment> attachments,
        EmailOptions options)
    {
        try
        {
            var message = new MimeMessage();

            // From
            message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));

            // To
            message.To.Add(MailboxAddress.Parse(to));

            // Reply-To
            if (!string.IsNullOrEmpty(options.ReplyTo))
            {
                message.ReplyTo.Add(MailboxAddress.Parse(options.ReplyTo));
            }

            // CC
            foreach (var cc in options.CC)
            {
                message.Cc.Add(MailboxAddress.Parse(cc));
            }

            // BCC
            foreach (var bcc in options.BCC)
            {
                message.Bcc.Add(MailboxAddress.Parse(bcc));
            }

            // Subject
            message.Subject = subject;

            // Body builder
            var builder = new BodyBuilder();

            if (options.IsHtml)
            {
                builder.HtmlBody = htmlBody;
            }
            else
            {
                builder.TextBody = htmlBody;
            }

            // Attachments
            foreach (var attachment in attachments)
            {
                if (attachment.Content != null)
                {
                    builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
                }
                else if (attachment.FileStream != null)
                {
                    builder.Attachments.Add(attachment.FileName, attachment.FileStream, ContentType.Parse(attachment.ContentType));
                }
            }

            message.Body = builder.ToMessageBody();

            // Send via SMTP
            var smtpSettings = options.SmtpSettings ?? GetDefaultSmtpSettings();

            using var client = new SmtpClient();

            // Connect
            await client.ConnectAsync(
                smtpSettings.Host,
                smtpSettings.Port,
                smtpSettings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None
            );

            // Authenticate
            if (!string.IsNullOrEmpty(smtpSettings.Username))
            {
                await client.AuthenticateAsync(smtpSettings.Username, smtpSettings.Password);
            }

            // Send
            await client.SendAsync(message);

            // Disconnect
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {To} with {AttachmentCount} attachments", to, attachments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            throw;
        }
    }

    private EmailOptions GetDefaultEmailOptions()
    {
        return new EmailOptions
        {
            FromAddress = _configuration["Email:FromAddress"] ?? "noreply@example.com",
            FromName = _configuration["Email:FromName"] ?? "PDF Reader Demo",
            SmtpSettings = GetDefaultSmtpSettings()
        };
    }

    private SmtpSettings GetDefaultSmtpSettings()
    {
        return new SmtpSettings
        {
            Host = _configuration["Email:Smtp:Host"] ?? "smtp.gmail.com",
            Port = int.Parse(_configuration["Email:Smtp:Port"] ?? "587"),
            Username = _configuration["Email:Smtp:Username"] ?? "",
            Password = _configuration["Email:Smtp:Password"] ?? "",
            EnableSsl = bool.Parse(_configuration["Email:Smtp:EnableSsl"] ?? "true"),
            TimeoutSeconds = int.Parse(_configuration["Email:Smtp:TimeoutSeconds"] ?? "30")
        };
    }
}
