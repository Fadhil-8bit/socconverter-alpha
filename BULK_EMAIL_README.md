# Bulk Email Merger - MVC Implementation Guide

## Overview
Bulk Email integrates with the PDF split workflow to group PDFs by debtor code and send per-debtor emails with attachments.

## Architecture
- MVC: Controllers + Views
- Services: IBulkEmailService (grouping/sending), IEmailSender (SMTP via MailKit)
- Models: BulkEmailSession, DebtorEmailGroup, BulkEmailResult, AttachmentFile; EmailOptions/EmailAttachment

## Integration Flow
1) Upload and split PDF (PdfController ? SplitResult view)
2) Click "Send Bulk Email" ? BulkEmailController.Initiate(folderId)
3) Preview groups, enter emails, edit template
4) Send ? Result summary

## Configuration
appsettings.json
```json
{
  "Email": {
    "FromAddress": "noreply@example.com",
    "FromName": "PDF Reader Demo",
    "MaxAttachmentSizeMB": "10",
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": "587",
      "Username": "",
      "Password": "",
      "EnableSsl": "true",
      "TimeoutSeconds": "30"
    }
  }
}
```
Use User Secrets for credentials:
```
dotnet user-secrets set "Email:Smtp:Username" "your-email@example.com"
dotnet user-secrets set "Email:Smtp:Password" "your-app-password"
```

## Usage
- Split a PDF ? SplitResult ? Send Bulk Email
- Preview: fill debtor email addresses, customize subject/body
- Send: emails delivered with all debtor attachments

## Status
- Build: Success
- Session expired: Fixed
- Email sending: Tested OK

## Future (Optional)
- Background jobs, retries, delivery tracking, address lookup
