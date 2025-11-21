namespace socconvertor.Models.BulkEmail;

/// <summary>
/// Represents a single PDF file attachment from a session folder
/// </summary>
public class AttachmentFile
{
    /// <summary>
    /// Original filename (e.g., "A123-XXX SOA 2401.pdf")
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to the file on disk
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// The session folder this file came from (e.g., "abc123-session-id")
    /// </summary>
    public string SourceSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Relative path within the session folder (for display purposes)
    /// </summary>
    public string RelativeSessionPath { get; set; } = string.Empty;

    /// <summary>
    /// Document type extracted from filename pattern (SOA, Invoice, Overdue)
    /// </summary>
    public DocumentType Type { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// When this file was discovered during scanning
    /// </summary>
    public DateTime FoundDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Human-readable file size (e.g., "1.2 MB")
    /// </summary>
    public string SizeFormatted
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
            return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
