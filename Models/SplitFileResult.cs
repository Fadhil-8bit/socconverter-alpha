namespace socconvertor.Models;

public enum DocumentType
{
    SOA,
    Invoice,
    Overdue,
    Unknown
}

public class SplitFileResult
{
    public string FileName { get; set; } = "";
    public string Date { get; set; } = "";
    public string Code { get; set; } = ""; // Generic: account code, debtor code, etc.
    public DocumentType Type { get; set; }
}