using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using socconvertor.Models.Email;
using MailKit;
using System.Net.Sockets;

namespace socconvertor.Services;

/// <summary>
/// Email sender implementation using MailKit for SMTP with transient retry support
/// </summary>
public class EmailSenderService : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailSenderService> _logger;
    private readonly IBulkEmailDispatchQueue? _queue;

    public EmailSenderService(IConfiguration configuration, ILogger<EmailSenderService> logger, IBulkEmailDispatchQueue? queue = null)
    {
        _configuration = configuration;
        _logger = logger;
        _queue = queue;
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var options = GetDefaultEmailOptions();
        await SendEmailWithAttachmentsAsync(to, subject, body, new List<EmailAttachment>(), options, cancellationToken);
    }

    public async Task SendEmailWithAttachmentsAsync(
        string to,
        string subject,
        string htmlBody,
        List<EmailAttachment> attachments,
        EmailOptions options,
        CancellationToken cancellationToken = default)
    {
        // Read retry configuration
        int maxAttempts = int.Parse(_configuration["Email:Retry:MaxAttempts"] ?? "3");
        int initialBackoffMs = int.Parse(_configuration["Email:Retry:InitialBackoffMs"] ?? "1000");
        int backoffFactor = int.Parse(_configuration["Email:Retry:BackoffFactor"] ?? "2");

        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Update attempt count in job item if queue is available
            if (_queue != null && !string.IsNullOrEmpty(options.JobId) && !string.IsNullOrEmpty(options.DebtorCode))
            {
                var job = _queue.GetJob(options.JobId);
                if (job != null)
                {
                    var item = job.Items.FirstOrDefault(i => i.DebtorCode == options.DebtorCode);
                    if (item != null)
                    {
                        item.AttemptCount = attempt;
                        item.LastAttemptUtc = DateTime.UtcNow;
                        _queue.UpdateJob(job);
                    }
                }
            }

            try
            {
                await SendEmailInternalAsync(to, subject, htmlBody, attachments, options, cancellationToken);
                
                if (attempt > 1)
                {
                    _logger.LogInformation("Email sent successfully to {To} on attempt {Attempt}/{MaxAttempts}", 
                        to, attempt, maxAttempts);
                }
                else
                {
                    _logger.LogInformation("Email sent successfully to {To} with {AttachmentCount} attachments", 
                        to, attachments.Count);
                }
                return; // Success
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Email send to {To} cancelled during attempt {Attempt}/{MaxAttempts}", 
                    to, attempt, maxAttempts);
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                bool isTransient = IsTransient(ex);

                if (!isTransient)
                {
                    _logger.LogError(ex, "Permanent error sending email to {To} - not retrying", to);
                    throw;
                }

                if (attempt < maxAttempts)
                {
                    int delayMs = initialBackoffMs * (int)Math.Pow(backoffFactor, attempt - 1);
                    _logger.LogWarning(ex, 
                        "Transient error sending email to {To} (attempt {Attempt}/{MaxAttempts}). Retrying after {DelayMs}ms...", 
                        to, attempt, maxAttempts, delayMs);
                    
                    try
                    {
                        await Task.Delay(delayMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Retry backoff cancelled for {To}", to);
                        throw;
                    }
                }
                else
                {
                    _logger.LogError(ex, 
                        "Failed to send email to {To} after {Attempts} attempts", 
                        to, maxAttempts);
                }
            }
        }

        // All retries exhausted
        throw lastException ?? new InvalidOperationException("Send failed with no exception captured");
    }

    private async Task SendEmailInternalAsync(
        string to,
        string subject,
        string htmlBody,
        List<EmailAttachment> attachments,
        EmailOptions options,
        CancellationToken cancellationToken)
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
            smtpSettings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken
        );

        // Authenticate
        if (!string.IsNullOrEmpty(smtpSettings.Username))
        {
            await client.AuthenticateAsync(smtpSettings.Username, smtpSettings.Password, cancellationToken);
        }

        // Send
        await client.SendAsync(message, cancellationToken);

        // Disconnect
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>
    /// Determines if an exception represents a transient error that should be retried
    /// </summary>
    private bool IsTransient(Exception ex)
    {
        // Check the exception itself
        if (IsTransientException(ex))
            return true;

        // Check inner exceptions recursively
        var inner = ex.InnerException;
        while (inner != null)
        {
            if (IsTransientException(inner))
                return true;
            inner = inner.InnerException;
        }

        return false;
    }

    private bool IsTransientException(Exception ex)
    {
        // Network-level transient errors
        if (ex is SocketException ||
            ex is IOException ||
            ex is TimeoutException ||
            ex is TaskCanceledException)
        {
            return true;
        }

        // MailKit SMTP transient errors (4xx response codes indicate temporary failures)
        if (ex is SmtpCommandException smtpCmd)
        {
            // 4xx codes are transient (temporary failures)
            // 5xx codes are permanent (mailbox not found, policy violations, etc.)
            return smtpCmd.StatusCode >= SmtpStatusCode.ServiceNotAvailable &&
                   smtpCmd.StatusCode < SmtpStatusCode.SyntaxError;
        }

        if (ex is SmtpProtocolException)
        {
            // Protocol errors (connection issues, malformed responses) are often transient
            return true;
        }

        if (ex is ServiceNotConnectedException ||
            ex is ServiceNotAuthenticatedException)
        {
            // Connection state errors can be transient
            return true;
        }

        return false;
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
