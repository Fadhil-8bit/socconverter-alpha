namespace socconvertor.Models.BulkEmail;

public class SelectSessionsViewModel
{
    public List<string> AvailableSessionIds { get; set; } = new();
    public List<string> SelectedSessionIds { get; set; } = new();

    public List<SessionInfo> Sessions { get; set; } = new();

    public string? Query { get; set; }
}

public class SessionInfo
{
    public string Id { get; set; } = string.Empty;
    public int PdfCount { get; set; }
    public long TotalBytes { get; set; }
    public DateTime LastModifiedUtc { get; set; }

    public int SoaCount { get; set; }
    public int InvoiceCount { get; set; }
    public int OverdueCount { get; set; }

    public string TotalSizeFormatted
    {
        get
        {
            var bytes = TotalBytes;
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
