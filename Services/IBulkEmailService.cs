using PdfReaderDemo.Models.BulkEmail;
using PdfReaderDemo.Models.Email;

namespace PdfReaderDemo.Services;

/// <summary>
/// Service for managing bulk email operations with PDF attachments
/// </summary>
public interface IBulkEmailService
{
    /// <summary>
    /// Scans multiple temp session folders, groups PDF files by debtor code, and prepares a bulk email session
    /// </summary>
    /// <param name="sessionIds">List of temp session folder IDs to scan (e.g., ["abc123", "def456"])</param>
    /// <returns>A BulkEmailSession with grouped debtor data ready for preview</returns>
    Task<BulkEmailSession> PrepareBulkEmailAsync(List<string> sessionIds);

    /// <summary>
    /// Sends emails to all debtors in a bulk email session
    /// </summary>
    /// <param name="bulkSessionId">The unique ID of the bulk email session</param>
    /// <param name="subject">Email subject line (supports placeholders like {DebtorCode})</param>
    /// <param name="bodyTemplate">Email body template (supports placeholders)</param>
    /// <param name="options">Email configuration options</param>
    /// <returns>Result summary with success/failure counts</returns>
    Task<BulkEmailResult> SendBulkEmailsAsync(string bulkSessionId, string subject, string bodyTemplate, EmailOptions options);

    /// <summary>
    /// Retrieves a bulk email session by ID
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The bulk email session or null if not found</returns>
    Task<BulkEmailSession?> GetSessionAsync(string sessionId);

    /// <summary>
    /// Updates email addresses for debtors in a session (before sending)
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="emailMappings">Dictionary of DebtorCode -> EmailAddress</param>
    /// <returns>True if successful</returns>
    Task<bool> UpdateEmailAddressesAsync(string sessionId, Dictionary<string, string> emailMappings);

    /// <summary>
    /// Extracts debtor code from a filename using regex patterns
    /// </summary>
    /// <param name="fileName">The filename to parse (e.g., "A123-XXX SOA 2401.pdf")</param>
    /// <returns>Debtor code or null if not found</returns>
    string? ExtractDebtorCodeFromFileName(string fileName);
}
