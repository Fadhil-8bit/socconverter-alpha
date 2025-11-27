using socconvertor.Models.Email;
using System.Threading;

namespace socconvertor.Services;

/// <summary>
/// Interface for sending emails with attachment support
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends a simple email without attachments
    /// </summary>
    Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email with multiple attachments
    /// </summary>
    /// <param name="to">Recipient email address</param>
    /// <param name="subject">Email subject</param>
    /// <param name="htmlBody">HTML body content</param>
    /// <param name="attachments">List of file attachments</param>
    /// <param name="options">Email options (from, reply-to, CC, etc.)</param>
    /// <param name="cancellationToken">Cancellation token for aborting send/retries</param>
    Task SendEmailWithAttachmentsAsync(
        string to,
        string subject,
        string htmlBody,
        List<EmailAttachment> attachments,
        EmailOptions options,
        CancellationToken cancellationToken = default);
}
