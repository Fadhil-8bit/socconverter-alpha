namespace PdfReaderDemo.Models
{
    public class SplitFileResult
    {
        public string FileName { get; set; } = "";
        public string Date { get; set; } = "";
        public string AccountCode { get; set; } = "";
    }

    public class InvoiceRecord
    {
        public string DebtorCode { get; set; } = "";
        public string Date { get; set; } = "";
        public int PageNumber { get; set; }
    }

}
    