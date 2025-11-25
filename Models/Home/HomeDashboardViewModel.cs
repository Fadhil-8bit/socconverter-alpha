namespace socconvertor.Models.Home;

using socconvertor.Helpers;

public class HomeDashboardViewModel
{
    public int SplitSessionsCount { get; set; }
    public int BulkSessionsCount { get; set; }
    public int SplitPdfCount { get; set; }
    public int BulkPdfCount { get; set; }
    public long TotalBytes { get; set; }
    public List<HomeSessionItem> RecentActivities { get; set; } = new();

    public int TotalSessions => SplitSessionsCount + BulkSessionsCount;
    public int TotalPdfs => SplitPdfCount + BulkPdfCount;
    public string TotalSizeFormatted => FormatHelpers.FormatBytes(TotalBytes);
}

public class HomeSessionItem
{
    public string Id { get; set; } = string.Empty;
    public int PdfCount { get; set; }
    public string TotalSizeFormatted { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
    public string Origin { get; set; } = "split"; // split | zip
}
