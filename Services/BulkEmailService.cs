using socconvertor.Models;
using socconvertor.Models.BulkEmail;
using socconvertor.Models.Email;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace socconvertor.Services;

public class BulkEmailService : IBulkEmailService
{
    private readonly IEmailSender _emailSender;
    private readonly IWebHostEnvironment _environment; // retained for potential future use
    private readonly IContactProvider _contacts;
    private readonly IStoragePaths _paths;
    private readonly ConcurrentDictionary<string, BulkEmailSession> _sessions;

    // Regex pattern to extract debtor code from filename
    // Matches patterns like: "A123-XXX", "12345-ABC", etc. (digits/letters + dash + alphanumeric)
    private static readonly Regex DebtorCodePattern = new(
        @"^([A-Z0-9]{3,5}-[A-Z0-9]{3,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500)
    );

    public BulkEmailService(IEmailSender emailSender, IWebHostEnvironment environment, IContactProvider contacts, IStoragePaths paths)
    {
        _emailSender = emailSender;
        _environment = environment;
        _contacts = contacts;
        _paths = paths;
        _sessions = new ConcurrentDictionary<string, BulkEmailSession>();
    }

    public async Task<BulkEmailSession> PrepareBulkEmailAsync(List<string> sessionIds)
    {
        var bulkSession = new BulkEmailSession
        {
            SourceSessionIds = sessionIds,
            Status = BulkEmailStatus.Draft
        };

        var attachmentsByDebtor = new Dictionary<string, List<AttachmentFile>>();

        // Scan each session folder
        foreach (var sessionId in sessionIds)
        {
            // Use configured bulk email root instead of hardcoded wwwroot/Temp
            var sessionPath = Path.Combine(_paths.BulkEmailRoot, sessionId);
            if (!Directory.Exists(sessionPath))
                continue;

            // Get all PDF files, excluding the "original" subfolder
            var pdfFiles = Directory.GetFiles(sessionPath, "*.pdf", SearchOption.TopDirectoryOnly);
            foreach (var filePath in pdfFiles)
            {
                var fileName = Path.GetFileName(filePath);

                // Skip files in "original" subfolder if they somehow got included
                if (filePath.Contains(Path.Combine(sessionPath, "original"), StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract debtor code
                var debtorCode = ExtractDebtorCodeFromFileName(fileName) ?? "UNCLASSIFIED";

                // Determine document type from filename
                var docType = DetermineDocumentType(fileName);

                // Create attachment file
                var fileInfo = new FileInfo(filePath);
                var attachment = new AttachmentFile
                {
                    FileName = fileName,
                    FilePath = filePath,
                    SourceSessionId = sessionId,
                    RelativeSessionPath = Path.Combine(sessionId, fileName),
                    Type = docType,
                    SizeBytes = fileInfo.Length,
                    FoundDate = DateTime.UtcNow
                };

                // Group by debtor code
                if (!attachmentsByDebtor.ContainsKey(debtorCode))
                    attachmentsByDebtor[debtorCode] = new List<AttachmentFile>();
                attachmentsByDebtor[debtorCode].Add(attachment);
            }
        }

        // Create debtor email groups (prefill email from contacts if available)
        foreach (var kvp in attachmentsByDebtor)
        {
            var prefill = _contacts.GetEmailForDebtor(kvp.Key) ?? string.Empty;
            bulkSession.DebtorGroups.Add(new DebtorEmailGroup
            {
                DebtorCode = kvp.Key,
                Attachments = kvp.Value,
                EmailAddress = prefill
            });
        }

        bulkSession.Status = BulkEmailStatus.Previewing;

        // Store session
        _sessions[bulkSession.SessionId] = bulkSession;

        return await Task.FromResult(bulkSession);
    }

    public async Task<BulkEmailResult> SendBulkEmailsAsync(
        string bulkSessionId,
        string subject,
        string bodyTemplate,
        EmailOptions options)
    {
        var result = new BulkEmailResult
        {
            StartedAt = DateTime.UtcNow
        };

        // Load session
        if (!_sessions.TryGetValue(bulkSessionId, out var session))
        {
            result.GeneralError = "Session not found";
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }

        session.Status = BulkEmailStatus.Sending;
        result.TotalAttempted = session.DebtorGroups.Count;

        // Send email to each debtor
        foreach (var group in session.DebtorGroups)
        {
            try
            {
                // Validate email address
                if (string.IsNullOrWhiteSpace(group.EmailAddress))
                {
                    result.FailedDebtorCodes.Add(group.DebtorCode);
                    result.FailureDetails[group.DebtorCode] = "Email address is empty";
                    continue;
                }

                // Check attachment size
                var totalSizeMB = group.TotalSizeBytes / (1024.0 * 1024.0);
                if (totalSizeMB > options.MaxAttachmentSizeMB)
                {
                    result.FailedDebtorCodes.Add(group.DebtorCode);
                    result.FailureDetails[group.DebtorCode] = $"Total attachment size ({totalSizeMB:F1} MB) exceeds limit ({options.MaxAttachmentSizeMB} MB)";
                    continue;
                }

                // Prepare attachments
                var emailAttachments = group.Attachments
                    .Select(a => EmailAttachment.FromFile(a.FilePath))
                    .ToList();

                // Replace placeholders in subject and body
                var personalizedSubject = ReplacePlaceholders(subject, group, options.Placeholders);
                var personalizedBody = ReplacePlaceholders(bodyTemplate, group, options.Placeholders);

                // Send email
                await _emailSender.SendEmailWithAttachmentsAsync(
                    group.EmailAddress,
                    personalizedSubject,
                    personalizedBody,
                    emailAttachments,
                    options
                );

                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailedDebtorCodes.Add(group.DebtorCode);
                result.FailureDetails[group.DebtorCode] = ex.Message;
            }
        }

        session.Status = result.IsSuccess ? BulkEmailStatus.Completed : BulkEmailStatus.Failed;
        session.CompletedAt = DateTime.UtcNow;
        result.CompletedAt = DateTime.UtcNow;

        return result;
    }

    public Task<BulkEmailSession?> GetSessionAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<bool> UpdateEmailAddressesAsync(string sessionId, Dictionary<string, string> emailMappings)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return Task.FromResult(false);

        foreach (var group in session.DebtorGroups)
        {
            if (emailMappings.TryGetValue(group.DebtorCode, out var addr))
                group.EmailAddress = addr;
        }
        return Task.FromResult(true);
    }

    public string? ExtractDebtorCodeFromFileName(string fileName)
    {
        var match = DebtorCodePattern.Match(fileName);
        return match.Success ? match.Groups[1].Value.Replace(" ", "").Trim() : null;
    }

    private static DocumentType DetermineDocumentType(string fileName)
    {
        var upper = fileName.ToUpperInvariant();

        if (upper.Contains(" SOA ") || upper.Contains("_SOA_"))
            return DocumentType.SOA;

        if (upper.Contains(" INV ") || upper.Contains("_INV_") || upper.Contains("INVOICE"))
            return DocumentType.Invoice;

        if (upper.Contains(" OD ") || upper.Contains("_OD_") || upper.Contains("OVERDUE"))
            return DocumentType.Overdue;

        return DocumentType.Invoice; // Default
    }

    private static string ReplacePlaceholders(string template, DebtorEmailGroup group, Dictionary<string, string> customPlaceholders)
    {
        var result = template
            .Replace("{DebtorCode}", group.DebtorCode)
            .Replace("{DebtorName}", group.DebtorName ?? group.DebtorCode)
            .Replace("{FileCount}", group.TotalFileCount.ToString())
            .Replace("{TotalSize}", group.TotalSizeFormatted);

        // Custom placeholders
        foreach (var kvp in customPlaceholders)
            result = result.Replace($"{{{kvp.Key}}}", kvp.Value);

        return result;
    }
}
