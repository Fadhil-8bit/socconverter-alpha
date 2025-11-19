# Bulk Email Merger - MVC Implementation Guide

## Overview
The Bulk Email Merger system integrates with your existing PDF split workflow, allowing you to scan session folders, group PDF files by debtor code, and send bulk emails with attachments to customers.

## Architecture: MVC Pattern

### Controllers (`Controllers/`)
- **BulkEmailController**: Main controller handling bulk email workflow
  - `Initiate(folderId)`: Quick bulk email from PDF split result page
  - `InitiateManual()`: Manual entry for multiple session folders
  - `Preview()`: Review and edit grouped emails
  - `Send()`: Send bulk emails

### Views (`Views/BulkEmail/`)
- **InitiateManual.cshtml**: Manual session ID entry form
- **Preview.cshtml**: Preview grouped files and edit email addresses
- **Result.cshtml**: Display send results

### Models (`Models/BulkEmail/` & `Models/Email/`)
- **DebtorEmailGroup**: Groups PDFs by debtor code
- **AttachmentFile**: Individual PDF metadata
- **BulkEmailSession**: Session tracking
- **BulkEmailStatus**: Workflow states enum
- **BulkEmailResult**: Send operation results
- **EmailAttachment**: File attachment wrapper
- **EmailOptions**: Email/SMTP configuration

### Services (`Services/`)
- **IBulkEmailService / BulkEmailService**: Core business logic
  - Folder scanning and file grouping
  - Debtor code extraction
  - Email sending coordination
- **IEmailSender / EmailSenderService**: MailKit SMTP implementation

## Integration with PDF Split Workflow

### Workflow Overview

```
1. User uploads PDF ? PdfController.Upload()
2. PDF is split ? PdfController returns SplitResult view
3. User clicks "Send Bulk Email" button
4. Redirects to BulkEmailController.Initiate(folderId)
5. System scans folder and groups PDFs by debtor code
6. User reviews/edits email addresses ? Preview view
7. User customizes email template
8. System sends emails ? Result view
```

### File Flow Diagram

```
wwwroot/Temp/
  ??? upload-abc123-20250118/          ? Session folder (folderId)
      ??? original/                    ? Original uploaded PDF (ignored)
      ?   ??? Invoice_Batch.pdf
      ??? A123-XXX SOA 2401.pdf       ? Split PDFs (scanned)
      ??? A123-XXX INV 2401.pdf
      ??? B456-YYY SOA 2401.pdf
      ??? C789-ZZZ OD.pdf

? BulkEmailService.PrepareBulkEmailAsync()

Grouped by Debtor Code:
  - A123-XXX: [SOA 2401.pdf, INV 2401.pdf]
  - B456-YYY: [SOA 2401.pdf]
  - C789-ZZZ: [OD.pdf]

? User enters email addresses

? BulkEmailService.SendBulkEmailsAsync()

Emails sent with attachments
```

## Features
- ? **Quick Access**: One-click bulk email from Split Result page
- ? **Single or Multi-Session**: Send from one upload or merge multiple
- ? **Automatic Grouping**: Groups PDFs by debtor code pattern
- ? **Preview & Edit**: Review groups and edit email addresses
- ? **Template Support**: Customizable subject/body with placeholders
- ? **Validation**: Email format and attachment size checks
- ? **Error Tracking**: Detailed per-debtor error messages
- ? **SMTP Support**: MailKit with Gmail, Outlook, or custom SMTP

## Usage

### Option 1: Quick Bulk Email (Recommended)

1. **Upload and split PDF** as normal via `/Pdf/Index`
2. On the **Split Result** page, click **"Send Bulk Email"** button
3. System automatically scans the session folder
4. **Preview** shows grouped files by debtor code
5. Enter **email addresses** for each debtor
6. Customize **email template** (optional)
7. Click **"Send All Emails"**
8. View **results** with success/failure details

### Option 2: Manual Multi-Session

1. Navigate to `/BulkEmail/InitiateManual`
2. Enter **multiple session folder IDs** (comma or newline separated)
3. Example:
   ```
   upload-abc123-20250118
   upload-def456-20250118
   upload-ghi789-20250118
   ```
4. Continue with Preview ? Send workflow

## Configuration

### appsettings.json

```json
{
  "Email": {
    "FromAddress": "noreply@example.com",
    "FromName": "Your Company Name",
    "MaxAttachmentSizeMB": "10",
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": "587",
      "Username": "your-email@gmail.com",
      "Password": "your-app-password",
      "EnableSsl": "true",
      "TimeoutSeconds": "30"
    }
  }
}
```

### User Secrets (Development)

```bash
dotnet user-secrets set "Email:Smtp:Username" "your-email@gmail.com"
dotnet user-secrets set "Email:Smtp:Password" "your-16-char-app-password"
```

### Gmail Setup

1. Enable **2-Factor Authentication**
2. Generate **App Password**: https://myaccount.google.com/apppasswords
3. Use App Password in configuration (not your regular password)

## Email Template Placeholders

Use these in subject and body:
- `{DebtorCode}` - Customer identifier (e.g., "A123-XXX")
- `{DebtorName}` - Display name (if set, defaults to DebtorCode)
- `{FileCount}` - Number of attachments
- `{TotalSize}` - Human-readable size (e.g., "2.3 MB")

### Example Template

**Subject:**
```
Your Documents - {DebtorCode}
```

**Body:**
```html
Dear Customer,

Please find attached {FileCount} document(s) for account {DebtorCode}.

Total attachment size: {TotalSize}

If you have any questions, please contact us.

Best regards,
Your Company
```

## Debtor Code Extraction

The system extracts debtor codes from filenames using this regex pattern:

```regex
^([A-Z0-9]{3,5}-[A-Z0-9]{3,})
```

### Valid Examples:
- `A123-XXX SOA 2401.pdf` ? **A123-XXX**
- `12345-ABC INV 2401.pdf` ? **12345-ABC**
- `B456-YYY OD.pdf` ? **B456-YYY**

### Invalid Examples:
- `Invoice_2024.pdf` ? **UNCLASSIFIED** (no debtor code pattern)
- `123.pdf` ? **UNCLASSIFIED**

### Document Type Detection:
- Contains " SOA " or "_SOA_" ? **DocumentType.SOA**
- Contains " INV " or "_INV_" or "INVOICE" ? **DocumentType.Invoice**
- Contains " OD " or "_OD_" or "OVERDUE" ? **DocumentType.Overdue**

## Code Structure

### BulkEmailController Actions

```csharp
// Quick bulk email from split result
GET  /BulkEmail/Initiate?folderId=upload-abc123-20250118

// Manual multi-session entry
GET  /BulkEmail/InitiateManual
POST /BulkEmail/InitiateManual

// Preview grouped files
GET  /BulkEmail/Preview

// Send emails
POST /BulkEmail/Send
```

### Service Registration (Program.cs)

```csharp
builder.Services.AddSingleton<IBulkEmailService, BulkEmailService>();
builder.Services.AddSingleton<IEmailSender, EmailSenderService>();
```

## Validation & Error Handling

### Email Address Validation
- **Client-side**: JavaScript regex on blur
- **Server-side**: MailKit validates before sending
- **Invalid emails**: Skipped with error message

### Attachment Size Limits
- **Default**: 10 MB per email
- **Configurable**: `Email:MaxAttachmentSizeMB` in config
- **Exceeded**: Email skipped with error message

### Folder Scanning
- **Non-existent folders**: Skipped silently
- **`/original/` subfolders**: Automatically ignored
- **No PDFs found**: Error message displayed

### Error Tracking
- **Per-debtor failures**: Stored in `BulkEmailResult.FailureDetails`
- **General errors**: Stored in `BulkEmailResult.GeneralError`
- **Logged**: All errors logged via `ILogger`

## Security Considerations

### SMTP Credentials
- ? Use **User Secrets** for development
- ? Use **Azure Key Vault** or environment variables for production
- ? **Never commit** credentials to source control

### File Path Validation
- ? `folderId` validated with regex: `^[a-zA-Z0-9_\-]+$`
- ? Prevents path traversal attacks
- ? Uses `Path.Combine()` and `Path.GetFileName()`

### Email Rate Limiting
Consider implementing:
- Delays between sends
- Background job queue (Hangfire, etc.)
- Maximum emails per session

## Troubleshooting

### "Session expired" Error
**Cause:** TempData expires after redirect  
**Solution:** Session data is stored in memory; use within 5 minutes

### "No PDF files found"
**Cause:** Wrong folderId or files in `/original/` subfolder  
**Solution:** Check folder path in `wwwroot/Temp/`

### SMTP Authentication Failed
**Gmail:** Use App Password, not regular password  
**Outlook:** Ensure account allows SMTP access  
**Check:** Username/password are correct in config

### Emails Not Received
**Check:**
1. Spam/junk folders
2. Firewall allows port 587/465
3. SMTP host/port are correct
4. FromAddress is valid

## Testing Checklist

- [ ] Split PDF and verify session folder created
- [ ] Click "Send Bulk Email" button on result page
- [ ] Verify files are grouped correctly by debtor code
- [ ] Enter test email addresses
- [ ] Customize email template
- [ ] Send test emails
- [ ] Verify emails received with correct attachments
- [ ] Test error handling (invalid email, file too large)
- [ ] Test multi-session manual entry

## Dependencies

- **MailKit** (v4.14.1): SMTP email sending
- **iText7**: PDF file handling (existing)
- **ASP.NET Core 9.0**: MVC framework
- **Bootstrap 5**: UI components (existing)

## File Summary

### Created/Modified Files

**Controllers:** 1 new
- `Controllers/BulkEmailController.cs`

**Views:** 3 new, 1 modified
- `Views/BulkEmail/InitiateManual.cshtml`
- `Views/BulkEmail/Preview.cshtml`
- `Views/BulkEmail/Result.cshtml`
- `Views/Pdf/SplitResult.cshtml` (modified - added button)

**Models:** 7 new
- `Models/BulkEmail/DebtorEmailGroup.cs`
- `Models/BulkEmail/AttachmentFile.cs`
- `Models/BulkEmail/BulkEmailSession.cs`
- `Models/BulkEmail/BulkEmailStatus.cs`
- `Models/BulkEmail/BulkEmailResult.cs`
- `Models/Email/EmailAttachment.cs`
- `Models/Email/EmailOptions.cs`

**Services:** 4 new
- `Services/IBulkEmailService.cs`
- `Services/BulkEmailService.cs`
- `Services/IEmailSender.cs`
- `Services/EmailSenderService.cs`

**Configuration:** 2 modified
- `Program.cs` (service registration)
- `appsettings.json` (email settings)

**Total:** 16 new files, 3 modified files

## Future Enhancements

### Planned Features
- [ ] Database lookup for debtor email addresses
- [ ] Background job queue for large batches
- [ ] Email preview modal before sending
- [ ] CSV import for email addresses
- [ ] Retry failed sends
- [ ] Email delivery tracking
- [ ] Schedule send for later

### Performance Optimization
- [ ] Async file scanning
- [ ] Lazy loading of attachments
- [ ] Session data persistence (Redis)
- [ ] Parallel email sending

## License
[Your License Here]

## Support
For issues or questions, contact: [Your Contact Info]

---

**Implementation Date:** January 2025  
**Version:** 1.0.0 (MVC)  
**Architecture:** ASP.NET Core MVC with Controllers and Views
