namespace socconvertor.Models.Email;

/// <summary>
/// Request payload for atomic queued send via JSON
/// </summary>
public class StartQueuedSendRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? BodyTemplate { get; set; }
    public Dictionary<string, string>? Mappings { get; set; }
}
