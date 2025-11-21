namespace socconvertor.Helpers;

/// <summary>
/// Small helper utilities used for formatting values in the UI and models.
/// </summary>
public static class FormatHelpers
{
    /// <summary>
    /// Formats a byte count into a short human-readable string (B, KB, MB, GB).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }
}
