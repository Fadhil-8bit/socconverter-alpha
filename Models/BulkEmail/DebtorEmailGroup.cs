namespace socconvertor.Models.BulkEmail;

using socconvertor.Helpers;

/// <summary>
/// Represents a group of PDF attachments for a single debtor/customer
/// </summary>
public class DebtorEmailGroup
{
    /// <summary>
    /// The customer identifier extracted from filename prefix (e.g., "A123" from "A123 SOA 2401.pdf")
    /// </summary>
    public string DebtorCode { get; set; } = string.Empty;

    /// <summary>
    /// Optional display name for the debtor
    /// </summary>
    public string? DebtorName { get; set; }

    /// <summary>
    /// Target email address for this debtor
    /// </summary>
    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// Collection of PDF files to attach to the email
    /// </summary>
    public List<AttachmentFile> Attachments { get; set; } = new();

    /// <summary>
    /// Total number of files for this debtor
    /// </summary>
    public int TotalFileCount => Attachments.Count;

    /// <summary>
    /// Combined size of all attachments in bytes
    /// </summary>
    public long TotalSizeBytes => Attachments.Sum(a => a.SizeBytes);

    /// <summary>
    /// Human-readable size (e.g., "2.3 MB")
    /// </summary>
    public string TotalSizeFormatted => FormatHelpers.FormatBytes(TotalSizeBytes);
}
