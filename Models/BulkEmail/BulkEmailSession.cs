namespace socconvertor.Models.BulkEmail;

using socconvertor.Helpers;

/// <summary>
/// Represents a bulk email operation session, tracking all debtors and their grouped files
/// </summary>
public class BulkEmailSession
{
    /// <summary>
    /// Unique identifier for this bulk email operation
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// List of source temp session folders being processed (e.g., ["session1", "session2"])
    /// </summary>
    public List<string> SourceSessionIds { get; set; } = new();

    /// <summary>
    /// Grouped debtor data with their attachments
    /// </summary>
    public List<DebtorEmailGroup> DebtorGroups { get; set; } = new();

    /// <summary>
    /// When this bulk email session was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current status of the bulk email operation
    /// </summary>
    public BulkEmailStatus Status { get; set; } = BulkEmailStatus.Draft;

    /// <summary>
    /// Total number of unique debtors in this session
    /// </summary>
    public int TotalDebtors => DebtorGroups.Count;

    /// <summary>
    /// Total number of PDF files across all debtors
    /// </summary>
    public int TotalFiles => DebtorGroups.Sum(g => g.TotalFileCount);

    /// <summary>
    /// Total size of all attachments in bytes
    /// </summary>
    public long TotalSizeBytes => DebtorGroups.Sum(g => g.TotalSizeBytes);

    /// <summary>
    /// Human-readable total size
    /// </summary>
    public string TotalSizeFormatted => FormatHelpers.FormatBytes(TotalSizeBytes);

    /// <summary>
    /// Error message if the session failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When the session was completed (null if still in progress)
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}
