namespace PdfReaderDemo.Models.Email;

/// <summary>
/// Represents an email attachment with file content
/// </summary>
public class EmailAttachment
{
    /// <summary>
    /// Display filename for the attachment (e.g., "Invoice_2024.pdf")
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// File content as byte array
    /// </summary>
    public byte[]? Content { get; set; }

    /// <summary>
    /// Alternative: Stream for large files (use either Content or FileStream, not both)
    /// </summary>
    public Stream? FileStream { get; set; }

    /// <summary>
    /// MIME content type (default: "application/pdf")
    /// </summary>
    public string ContentType { get; set; } = "application/pdf";

    /// <summary>
    /// File size in bytes (calculated from Content or FileStream)
    /// </summary>
    public long SizeBytes
    {
        get
        {
            if (Content != null) return Content.Length;
            if (FileStream != null && FileStream.CanSeek) return FileStream.Length;
            return 0;
        }
    }

    /// <summary>
    /// Creates an EmailAttachment from a file path
    /// </summary>
    public static EmailAttachment FromFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return new EmailAttachment
        {
            FileName = fileInfo.Name,
            Content = File.ReadAllBytes(filePath),
            ContentType = "application/pdf"
        };
    }

    /// <summary>
    /// Creates an EmailAttachment from a file stream (for large files)
    /// </summary>
    public static EmailAttachment FromStream(string fileName, Stream stream, string contentType = "application/pdf")
    {
        return new EmailAttachment
        {
            FileName = fileName,
            FileStream = stream,
            ContentType = contentType
        };
    }
}
