namespace socconvertor.Models.Home;

using socconvertor.Helpers;

public class HomeDashboardViewModel
{
    public int TotalSessions { get; set; }
    public int TotalPdfs { get; set; }
    public long TotalBytes { get; set; }
    public List<HomeSessionItem> RecentSessions { get; set; } = new();

    public string TotalSizeFormatted => FormatHelpers.FormatBytes(TotalBytes);
}

public class HomeSessionItem
{
    public string Id { get; set; } = string.Empty;
    public int PdfCount { get; set; }
    public string TotalSizeFormatted { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
}
