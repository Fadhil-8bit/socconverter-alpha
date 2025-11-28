namespace socconvertor.Models.BulkEmail;

public class EmailTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Template";
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool DefaultForSoaInvoice { get; set; }
    public bool DefaultForOverdue { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}
