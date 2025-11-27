using System.Text.Json;
using System.Text.Json.Serialization;

namespace socconvertor.Models.Email;

public enum EmailDispatchItemStatus
{
    Pending,
    Sending,
    Sent,
    Failed,
    Deferred
}

public class EmailDispatchItem
{
    public string DebtorCode { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public int AttachmentCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<string> AttachmentPaths { get; set; } = new();
    public EmailDispatchItemStatus Status { get; set; } = EmailDispatchItemStatus.Pending;
    public string? Error { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public int AttemptCount { get; set; }
}

public enum EmailDispatchJobStatus
{
    Queued,
    Running,
    Completed,
    PartiallyDeferred,
    Failed,
    Cancelled
}

public class EmailDispatchJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public EmailDispatchJobStatus Status { get; set; } = EmailDispatchJobStatus.Queued;
    public List<EmailDispatchItem> Items { get; set; } = new();
    public int SuccessCount => Items.Count(i => i.Status == EmailDispatchItemStatus.Sent);
    public int FailedCount => Items.Count(i => i.Status == EmailDispatchItemStatus.Failed);
    public int DeferredCount => Items.Count(i => i.Status == EmailDispatchItemStatus.Deferred);
    public int PendingCount => Items.Count(i => i.Status == EmailDispatchItemStatus.Pending || i.Status == EmailDispatchItemStatus.Sending);
    public int Total => Items.Count;
    public string? FailureReason { get; set; }
    public DateTime? NextResumeUtc { get; set; }
    public int ConsecutiveFailures { get; set; }

    public string SubjectTemplate { get; set; } = "Documents for {DebtorCode}";
    public string BodyTemplate { get; set; } = "Dear Customer,<br/>Please find attached {FileCount} document(s) for account {DebtorCode}.<br/>Total size: {TotalSize}<br/>Regards";

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static EmailDispatchJob? FromJson(string json) => JsonSerializer.Deserialize<EmailDispatchJob>(json);

    public void Cancel(string reason = "Cancelled by user")
    {
        Status = EmailDispatchJobStatus.Cancelled;
        FailureReason = reason;
        foreach (var item in Items.Where(i => i.Status == EmailDispatchItemStatus.Pending || i.Status == EmailDispatchItemStatus.Deferred || i.Status == EmailDispatchItemStatus.Sending))
        {
            item.Status = EmailDispatchItemStatus.Failed;
            item.Error = "Cancelled";
        }
        CompletedUtc = DateTime.UtcNow;
    }
}
