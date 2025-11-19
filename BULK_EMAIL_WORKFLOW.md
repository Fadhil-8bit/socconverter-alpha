# Bulk Email Merger - Workflow Integration

## Visual Workflow Diagram

```
???????????????????????????????????????????????????????????????????????
?                    PDF SPLIT WORKFLOW (Existing)                    ?
???????????????????????????????????????????????????????????????????????

    User
     ?
     ??? 1. Navigate to /Pdf/Index
     ?
     ??? 2. Upload PDF file
     ?
     ??? 3. Select split type (SOA / Invoice / Debtor Code)
     ?
     ??? 4. Click "Upload and Split"
          ?
          ??? PdfController.Upload(pdfFile, splitType, customCode)
          ?    ?
          ?    ?? Create session folder: wwwroot/Temp/upload-{guid}-{date}/
          ?    ?? Save original PDF to: upload-{guid}-{date}/original/
          ?    ?? Extract records using PdfService
          ?    ?? Split PDF by type
          ?    ?? Save split PDFs to session folder
          ?
          ??? Returns: SplitResult View
               ?
               ?  ????????????????????????????????????????????????????
               ?  ?  Split Result Page (Views/Pdf/SplitResult.cshtml)?
               ?  ?                                                   ?
               ?  ?  [Download All]  [Send Bulk Email]  [Upload More]?
               ?  ?                        ?                          ?
               ?  ?  Table of split files  ?  ? NEW INTEGRATION      ?
               ?  ?????????????????????????????????????????????????????
               ?                           ?
               ?                           ?
               ?                           ?
??????????????????????????????????????????????????????????????????????????
?              ?    BULK EMAIL WORKFLOW    ?       (New Feature)         ?
??????????????????????????????????????????????????????????????????????????
               ?                           ?
               ?                           ?
               ??? Click "Send Bulk Email" button
               ?   (passes folderId to BulkEmailController)
               ?
               ??? BulkEmailController.Initiate(folderId)
                    ?
                    ?? BulkEmailService.PrepareBulkEmailAsync([folderId])
                    ?   ?
                    ?   ?? Scan wwwroot/Temp/{folderId}/ for *.pdf
                    ?   ?? Ignore /original/ subfolder
                    ?   ?? Extract debtor code from each filename
                    ?   ?? Group files by debtor code
                    ?   ?? Create BulkEmailSession with DebtorEmailGroups
                    ?
                    ??? Returns: Preview View (BulkEmail/Preview.cshtml)
                         ?
                         ?  ???????????????????????????????????????????
                         ?  ?  Preview Page                           ?
                         ?  ?                                         ?
                         ?  ?  Summary: X Debtors, Y Files, Z Size   ?
                         ?  ?                                         ?
                         ?  ?  Table:                                 ?
                         ?  ?  ?????????????????????????????????    ?
                         ?  ?  ?Debtor?Email     ?Files ?Size  ?    ?
                         ?  ?  ?????????????????????????????????    ?
                         ?  ?  ?A123  ?[input]   ?  3   ?2.1MB ?    ?
                         ?  ?  ?B456  ?[input]   ?  2   ?1.5MB ?    ?
                         ?  ?  ?????????????????????????????????    ?
                         ?  ?                                         ?
                         ?  ?  Email Template:                        ?
                         ?  ?  Subject: [input with placeholders]    ?
                         ?  ?  Body:    [textarea with placeholders] ?
                         ?  ?                                         ?
                         ?  ?  [Send All Emails]  [Cancel]           ?
                         ?  ???????????????????????????????????????????
                         ?
                         ??? User fills email addresses and clicks "Send All Emails"
                              ?
                              ??? BulkEmailController.Send(...)
                                   ?
                                   ?? UpdateEmailAddressesAsync(emailMappings)
                                   ?? Prepare EmailOptions with SMTP settings
                                   ?
                                   ??? BulkEmailService.SendBulkEmailsAsync(...)
                                        ?
                                        ?? For each DebtorEmailGroup:
                                           ?
                                           ?? Validate email address
                                           ?? Check attachment size limit
                                           ?? Load PDF files as EmailAttachments
                                           ?? Replace placeholders in template
                                           ?
                                           ??? EmailSenderService.SendEmailWithAttachmentsAsync(...)
                                                ?
                                                ?? Build MimeMessage with MailKit
                                                ?? Add attachments
                                                ?? Connect to SMTP server
                                                ?? Authenticate
                                                ?? Send email
                                                ?? Log result (success/failure)
                                   
                                   Returns: Result View (BulkEmail/Result.cshtml)
                                   ?
                                   ?  ???????????????????????????????????????
                                   ?  ?  Result Page                        ?
                                   ?  ?                                     ?
                                   ?  ?  ? Success: X emails sent           ?
                                   ?  ?  ? Failed: Y emails                 ?
                                   ?  ?                                     ?
                                   ?  ?  Summary Cards:                     ?
                                   ?  ?  [Total] [Success] [Failed] [Time] ?
                                   ?  ?                                     ?
                                   ?  ?  Failed Emails Table (if any)      ?
                                   ?  ?                                     ?
                                   ?  ?  [Start New] [Upload More PDFs]    ?
                                   ?  ???????????????????????????????????????
```

## Alternative Entry Point: Manual Multi-Session

```
User navigates to /BulkEmail/InitiateManual
  ?
  ??? InitiateManual View (manual session ID entry)
       ?
       ?? User enters multiple session IDs:
       ?   upload-abc123-20250118
       ?   upload-def456-20250118
       ?   upload-ghi789-20250118
       ?
       ??? BulkEmailController.InitiateManual(sessionIds)
            ?
            ??? Continues to Preview ? Send ? Result
                (same as above)
```

## Data Flow

### Step 1: File Scanning
```
Input: folderId = "upload-abc123-20250118"

Scan: wwwroot/Temp/upload-abc123-20250118/
  ?? original/
  ?   ?? Invoice_Batch.pdf          ? IGNORED
  ?? A123-XXX SOA 2401.pdf           ? SCANNED
  ?? A123-XXX INV 2401.pdf           ? SCANNED
  ?? B456-YYY SOA 2401.pdf           ? SCANNED
  ?? C789-ZZZ OD.pdf                 ? SCANNED

Output: List<AttachmentFile>
```

### Step 2: Debtor Grouping
```
Extraction using regex: ^([A-Z0-9]{3,5}-[A-Z0-9]{3,})

Grouped:
  DebtorEmailGroup {
    DebtorCode: "A123-XXX"
    Attachments: [
      { FileName: "A123-XXX SOA 2401.pdf", Type: SOA, Size: 1.2MB },
      { FileName: "A123-XXX INV 2401.pdf", Type: Invoice, Size: 0.9MB }
    ]
    EmailAddress: "" (to be filled by user)
  }

  DebtorEmailGroup {
    DebtorCode: "B456-YYY"
    Attachments: [
      { FileName: "B456-YYY SOA 2401.pdf", Type: SOA, Size: 1.5MB }
    ]
    EmailAddress: ""
  }

  DebtorEmailGroup {
    DebtorCode: "C789-ZZZ"
    Attachments: [
      { FileName: "C789-ZZZ OD.pdf", Type: Overdue, Size: 0.8MB }
    ]
    EmailAddress: ""
  }
```

### Step 3: Email Sending
```
For DebtorEmailGroup "A123-XXX":
  ?
  ?? Subject: "Your Documents - A123-XXX"
  ?? Body: "Dear Customer, Please find attached 2 document(s)..."
  ?? Attachments:
  ?   ?? A123-XXX SOA 2401.pdf (1.2MB)
  ?   ?? A123-XXX INV 2401.pdf (0.9MB)
  ?? To: customer-A123@example.com
  ?
  ??? SMTP Send via MailKit
       ?? Success: Logged, Result.SuccessCount++
       ?? Failure: Logged, Result.FailureDetails["A123-XXX"] = error message
```

## Controller Action Flow

```csharp
// Quick entry from Split Result page
GET /BulkEmail/Initiate?folderId=upload-abc123-20250118
  ? BulkEmailController.Initiate(folderId)
    ? PrepareBulkEmailAsync([folderId])
      ? RedirectToAction("Preview")

// Preview grouped files
GET /BulkEmail/Preview
  ? BulkEmailController.Preview()
    ? GetSessionAsync(sessionId from TempData)
      ? Returns View(BulkEmailSession)

// Send emails
POST /BulkEmail/Send
  ? BulkEmailController.Send(sessionId, debtorCodes, emailAddresses, subject, bodyTemplate)
    ? UpdateEmailAddressesAsync(emailMappings)
    ? SendBulkEmailsAsync(sessionId, subject, bodyTemplate, options)
      ? Returns View("Result", BulkEmailResult)
```

## Service Layer Interaction

```
BulkEmailController
  ?
  ??? IBulkEmailService (business logic)
  ?    ?
  ?    ?? PrepareBulkEmailAsync()
  ?    ?   ?? Scans folders, groups by debtor code
  ?    ?
  ?    ?? SendBulkEmailsAsync()
  ?    ?   ?? Coordinates email sending
  ?    ?
  ?    ??? IEmailSender (SMTP implementation)
  ?         ?
  ?         ?? SendEmailWithAttachmentsAsync()
  ?             ?? MailKit SMTP client
  ?
  ??? IConfiguration (settings)
       ?? Email:Smtp:* configuration
```

## File System Layout

```
Project Root/
?
?? Controllers/
?   ?? PdfController.cs           (existing - modified)
?   ?? BulkEmailController.cs     (NEW)
?
?? Views/
?   ?? Pdf/
?   ?   ?? SplitResult.cshtml     (existing - modified with button)
?   ?? BulkEmail/                 (NEW)
?       ?? InitiateManual.cshtml
?       ?? Preview.cshtml
?       ?? Result.cshtml
?
?? Models/
?   ?? BulkEmail/                 (NEW)
?   ?   ?? DebtorEmailGroup.cs
?   ?   ?? AttachmentFile.cs
?   ?   ?? BulkEmailSession.cs
?   ?   ?? BulkEmailStatus.cs
?   ?   ?? BulkEmailResult.cs
?   ?? Email/                     (NEW)
?       ?? EmailAttachment.cs
?       ?? EmailOptions.cs
?
?? Services/
?   ?? PdfService.cs              (existing)
?   ?? IBulkEmailService.cs       (NEW)
?   ?? BulkEmailService.cs        (NEW)
?   ?? IEmailSender.cs            (NEW)
?   ?? EmailSenderService.cs      (NEW)
?
?? wwwroot/
    ?? Temp/
        ?? upload-{guid}-{date}/  (session folders)
            ?? original/          (ignored by bulk email)
            ?? *.pdf              (scanned and grouped)
```

## Integration Points Summary

| Component | Integration Point | Purpose |
|-----------|-------------------|---------|
| **SplitResult View** | "Send Bulk Email" button | Entry to bulk email workflow |
| **PdfController** | Creates session folder | Stores split PDFs for bulk email |
| **BulkEmailService** | Scans session folder | Groups PDFs by debtor code |
| **EmailSenderService** | Sends emails | Delivers PDFs via SMTP |
| **TempData** | Passes session ID | Maintains state between actions |

## User Experience Flow

```
1. User uploads PDF      ? "I have a batch invoice PDF"
2. PDF is split          ? "Great! Here are 10 split files"
3. User clicks button    ? "Send these via email instead of downloading"
4. System groups files   ? "Found 3 customers with these files"
5. User enters emails    ? "customer1@example.com, customer2@example.com, ..."
6. Emails are sent       ? "3 emails sent successfully!"
```

---

**Key Benefit:** Seamless integration - users can go from PDF upload to bulk email in 3 clicks!
