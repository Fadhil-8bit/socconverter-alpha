namespace PdfReaderDemo.Models.BulkEmail;

/// <summary>
/// Result of a bulk email send operation
/// </summary>
public class BulkEmailResult
{
    /// <summary>
    /// Total number of emails attempted
    /// </summary>
    public int TotalAttempted { get; set; }

    /// <summary>
    /// Number of emails sent successfully
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of emails that failed
    /// </summary>
    public int FailedCount => TotalAttempted - SuccessCount;

    /// <summary>
    /// List of debtor codes that failed to send
    /// </summary>
    public List<string> FailedDebtorCodes { get; set; } = new();

    /// <summary>
    /// Detailed error messages for failed sends (DebtorCode -> Error Message)
    /// </summary>
    public Dictionary<string, string> FailureDetails { get; set; } = new();

    /// <summary>
    /// Overall success status
    /// </summary>
    public bool IsSuccess => FailedCount == 0;

    /// <summary>
    /// When the bulk send operation started
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the bulk send operation completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration of the send operation
    /// </summary>
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;

    /// <summary>
    /// General error message if the entire operation failed
    /// </summary>
    public string? GeneralError { get; set; }
}
