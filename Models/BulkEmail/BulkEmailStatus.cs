namespace socconvertor.Models.BulkEmail;

/// <summary>
/// Status of a bulk email operation
/// </summary>
public enum BulkEmailStatus
{
    /// <summary>
    /// Initial state - session created but not yet ready for preview
    /// </summary>
    Draft,

    /// <summary>
    /// Files have been scanned and grouped, ready for user preview
    /// </summary>
    Previewing,

    /// <summary>
    /// Emails are currently being sent
    /// </summary>
    Sending,

    /// <summary>
    /// All emails have been sent successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Operation failed or was cancelled
    /// </summary>
    Failed
}
