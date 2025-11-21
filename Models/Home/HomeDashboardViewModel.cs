namespace socconvertor.Models.Home;

public class HomeDashboardViewModel
{
    public int TotalSessions { get; set; }
    public int TotalPdfs { get; set; }
    public long TotalBytes { get; set; }
    public List<HomeSessionItem> RecentSessions { get; set; } = new();

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

public class HomeSessionItem
{
    public string Id { get; set; } = string.Empty;
    public int PdfCount { get; set; }
    public string TotalSizeFormatted { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
}
