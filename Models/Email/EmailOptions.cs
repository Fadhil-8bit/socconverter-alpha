namespace socconvertor.Models.Email;

/// <summary>
/// Configuration options for sending emails
/// </summary>
public class EmailOptions
{
    /// <summary>
    /// Sender email address
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Sender display name
    /// </summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the email body is HTML formatted
    /// </summary>
    public bool IsHtml { get; set; } = true;

    /// <summary>
    /// Maximum total attachment size per email in MB (default: 10MB)
    /// </summary>
    public int MaxAttachmentSizeMB { get; set; } = 10;

    /// <summary>
    /// Reply-to email address (optional)
    /// </summary>
    public string? ReplyTo { get; set; }

    /// <summary>
    /// Carbon copy recipients (optional)
    /// </summary>
    public List<string> CC { get; set; } = new();

    /// <summary>
    /// Blind carbon copy recipients (optional)
    /// </summary>
    public List<string> BCC { get; set; } = new();

    /// <summary>
    /// Template placeholders for dynamic content substitution
    /// Example: {{"DebtorCode", "A123"}, {"FileCount", "5"}}
    /// </summary>
    public Dictionary<string, string> Placeholders { get; set; } = new();

    /// <summary>
    /// SMTP server settings (if using SMTP)
    /// </summary>
    public SmtpSettings? SmtpSettings { get; set; }

    /// <summary>
    /// Delay in milliseconds between individual sends
    /// </summary>
    public int DelayMs { get; set; } = 0;

    /// <summary>
    /// Maximum number of emails to send per minute (0 = unlimited)
    /// </summary>
    public int MaxPerMinute { get; set; } = 0;

    /// <summary>
    /// Maximum number of emails to send per hour (0 = unlimited)
    /// </summary>
    public int MaxPerHour { get; set; } = 0;
}

/// <summary>
/// SMTP server configuration
/// </summary>
public class SmtpSettings
{
    /// <summary>
    /// SMTP server host (e.g., "smtp.gmail.com")
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port (default: 587 for TLS)
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Username for SMTP authentication
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password for SMTP authentication
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Enable SSL/TLS encryption
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// Timeout in seconds (default: 30)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
