namespace PdfReaderDemo.Models.BulkEmail;

public class SelectSessionsViewModel
{
    public List<string> AvailableSessionIds { get; set; } = new();
    public List<string> SelectedSessionIds { get; set; } = new();
}
