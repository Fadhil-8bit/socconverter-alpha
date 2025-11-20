namespace socconvertor.Models;

public class BreadcrumbItem
{
    public string Text { get; set; } = string.Empty;
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public bool Active { get; set; }
}
