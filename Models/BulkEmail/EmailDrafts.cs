namespace socconvertor.Models.BulkEmail;

public class EmailDrafts
{
    public string SoaInvSubject { get; set; } = "Documents for {DebtorCode}";
    public string SoaInvBody { get; set; } = "Dear Customer,<br/><br/>Please find attached {FileCount} document(s) for account {DebtorCode}.<br/>Total size: {TotalSize}<br/><br/>Regards";

    public string OverdueSubject { get; set; } = "Overdue Reminder for {DebtorCode}";
    public string OverdueBody { get; set; } = "Dear Customer,<br/><br/>This is a reminder that your account {DebtorCode} has overdue balances. Please find the statement attached.<br/><br/>Regards";
}
