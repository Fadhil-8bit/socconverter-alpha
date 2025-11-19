# ?? Troubleshooting Guide: "Session Expired" Error

## ? **ISSUE FIXED!**

The "Session expired. Please start again." error has been **fixed** with the latest update using `TempData.Peek()`.

---

## ?? **What Was the Problem?**

### **Root Cause: TempData Lifespan**

**TempData** in ASP.NET Core only survives **ONE read** by default. Here's what was happening:

```csharp
// Step 1: Initiate stores session ID
TempData["BulkSessionId"] = bulkSession.SessionId;
return RedirectToAction("Preview");

// Step 2: Preview reads TempData
var sessionId = TempData["BulkSessionId"] as string;
// ? TempData is now DELETED after reading!

if (string.IsNullOrEmpty(sessionId))
{
    // This triggers because TempData was consumed
    TempData["ErrorMessage"] = "Session expired. Please start again.";
    return RedirectToAction("Index", "Pdf");
}
```

### **Why This Happened:**

1. **Click "Send Bulk Email"** ? `Initiate()` stores session ID in TempData
2. **Redirect to Preview** ? TempData survives (first redirect)
3. **Preview reads TempData** ? TempData is consumed and deleted
4. **Any subsequent access** ? TempData is gone!

---

## ? **The Fix Applied**

### **Updated Code in `BulkEmailController.cs`:**

```csharp
[HttpGet]
public async Task<IActionResult> Preview()
{
    // ? Use TempData.Peek() instead of TempData["key"]
    // Peek reads WITHOUT clearing the value
    var sessionId = TempData.Peek("BulkSessionId") as string;

    if (string.IsNullOrEmpty(sessionId))
    {
        _logger.LogWarning("Preview called without BulkSessionId in TempData");
        TempData["ErrorMessage"] = "Session expired. Please start again by clicking 'Send Bulk Email' button on the split result page.";
        return RedirectToAction("Index", "Pdf");
    }

    _logger.LogInformation("Loading preview for session: {SessionId}", sessionId);

    var session = await _bulkEmailService.GetSessionAsync(sessionId);

    if (session == null)
    {
        _logger.LogWarning("Session not found: {SessionId}", sessionId);
        TempData["ErrorMessage"] = $"Session not found: {sessionId}. Please start again.";
        return RedirectToAction("Index", "Pdf");
    }

    // ? Keep TempData for potential POST back
    TempData.Keep("BulkSessionId");
    
    // ? Also pass via ViewBag as backup
    ViewBag.BulkSessionId = sessionId;

    _logger.LogInformation("Preview loaded successfully. Debtors: {DebtorCount}, Files: {FileCount}", 
        session.TotalDebtors, session.TotalFiles);

    return View(session);
}
```

### **Key Changes:**

| Before (? Broken) | After (? Fixed) |
|-------------------|-----------------|
| `TempData["BulkSessionId"]` | `TempData.Peek("BulkSessionId")` |
| Value consumed on read | Value preserved |
| No backup storage | Added `ViewBag.BulkSessionId` |
| No logging | Added detailed logging |

---

## ?? **How to Test the Fix**

### **Test Steps:**

1. **Run the application:**
```bash
dotnet run
```

2. **Upload and split a PDF:**
   - Go to http://localhost:5000/Pdf/Index
   - Upload a PDF file
   - Select split type (SOA/Invoice/Debtor Code)
   - Enter custom code (e.g., 2401)
   - Click "Upload & Split"

3. **Click "Send Bulk Email" button:**
   - You should see the **Preview page** with:
     - Session summary (total debtors, files, size)
     - Table with debtor groups
     - Email input fields
     - Email template section

4. **Expected Result:**
   - ? **Preview page loads successfully**
   - ? **No "Session expired" error**
   - ? **Debtor groups are displayed**

---

## ?? **What You Should See**

### **1. After Clicking "Send Bulk Email"**

**URL:** `http://localhost:5000/BulkEmail/Preview`

**Page Content:**
```
Bulk Email Preview
Review grouped files and configure email settings before sending

Session Summary
- Total Debtors: 3
- Total Files: 5
- Total Size: 2.5 MB
- Status: Previewing

Debtor Email Groups
????????????????????????????????????????????????????????????
? Debtor   ? Email Address   ? Files ? Total Size? Actions ?
????????????????????????????????????????????????????????????
? A123-XXX ? [input box]     ?   2   ? 1.2 MB    ? [View]  ?
? B456-YYY ? [input box]     ?   1   ? 0.8 MB    ? [View]  ?
? C789-ZZZ ? [input box]     ?   2   ? 0.5 MB    ? [View]  ?
????????????????????????????????????????????????????????????

Email Template
Subject: Your Documents - {DebtorCode}
Body: [textarea with template]

[Send All Emails] [Cancel]
```

### **2. If You Still See "Session Expired"**

Check the **console logs** for detailed information:

```
info: PdfReaderDemo.Controllers.BulkEmailController[0]
      Initiating bulk email for folder: upload-abc123-20250118
info: PdfReaderDemo.Controllers.BulkEmailController[0]
      Bulk email scan completed. Debtors found: 3, Total files: 5
info: PdfReaderDemo.Controllers.BulkEmailController[0]
      Redirecting to Preview with session ID: a1b2c3d4-e5f6-...
info: PdfReaderDemo.Controllers.BulkEmailController[0]
      Loading preview for session: a1b2c3d4-e5f6-...
info: PdfReaderDemo.Controllers.BulkEmailController[0]
      Preview loaded successfully. Debtors: 3, Files: 5
```

---

## ?? **Other Possible Issues**

### **Issue 1: No PDF Files Found**

**Error Message:**
```
Error: No PDF files found in session folder 'upload-abc123-20250118'. 
Make sure PDFs are in the main folder, not in /original/ subfolder.
```

**Cause:** 
- Split PDFs are in `/original/` subfolder (which is ignored)
- Folder structure is incorrect

**Solution:**
Check your folder structure:
```
wwwroot/Temp/upload-abc123-20250118/
  ?? original/                    ? IGNORED
  ?   ?? Invoice_Batch.pdf
  ?? A123-XXX SOA 2401.pdf        ? SCANNED ?
  ?? A123-XXX INV 2401.pdf        ? SCANNED ?
  ?? B456-YYY SOA 2401.pdf        ? SCANNED ?
```

**Expected:** PDFs should be in the **main folder**, not in `/original/`

---

### **Issue 2: Wrong Folder ID**

**Error Message:**
```
Error: No folder ID provided
```

**Cause:** 
- `folderId` parameter is missing in the URL
- Button link is broken

**Solution:**
Check the URL when you click "Send Bulk Email":
```
? Correct: /BulkEmail/Initiate?folderId=upload-abc123-20250118
? Wrong:   /BulkEmail/Initiate
```

If wrong, check `Views/Pdf/SplitResult.cshtml`:
```html
<a class="btn btn-success" 
   href="@Url.Action("Initiate", "BulkEmail", new { folderId = folderId })">
    <i class="bi bi-envelope"></i> Send Bulk Email
</a>
```

Make sure `ViewBag.FolderId` is set in `PdfController.Upload()`.

---

### **Issue 3: Session Service Not Registered**

**Error Message:**
```
InvalidOperationException: Unable to resolve service for type 
'PdfReaderDemo.Services.IBulkEmailService'
```

**Cause:** 
Services not registered in `Program.cs`

**Solution:**
Check `Program.cs` has these lines:
```csharp
builder.Services.AddSingleton<IBulkEmailService, BulkEmailService>();
builder.Services.AddSingleton<IEmailSender, EmailSenderService>();
```

---

### **Issue 4: In-Memory Session Lost (App Restart)**

**Symptom:**
- Preview page works initially
- After app restart, "Session not found" error appears

**Cause:**
- `BulkEmailService` uses **in-memory** `ConcurrentDictionary`
- Sessions are lost when app restarts

**Solution (For Production):**
Consider using persistent storage:
```csharp
// Option 1: Use IDistributedCache (Redis, SQL Server)
// Option 2: Use database (EF Core)
// Option 3: Use session state with persistent storage
```

**For Development:**
This is normal behavior. Just click "Send Bulk Email" again.

---

## ?? **Expected Workflow (Without SMTP)**

### **Steps 1-6: No SMTP Needed ?**

| Step | Action | SMTP Required? | Status |
|------|--------|----------------|--------|
| 1 | Click "Send Bulk Email" | ? No | Should work |
| 2 | Scan folder | ? No | Should work |
| 3 | Group by debtor code | ? No | Should work |
| 4 | Show Preview page | ? No | **Should work now!** |
| 5 | Enter email addresses | ? No | Should work |
| 6 | Customize template | ? No | Should work |
| 7 | Click "Send All Emails" | ? **YES** | Will fail without SMTP |

### **Step 7: SMTP Error (Expected)**

When you click "Send All Emails" without SMTP configured:

```
Error sending emails: Authentication failed
```

Or:

```
Error sending emails: Unable to connect to SMTP server
```

**This is normal!** You'll see this on the **Result page**, not be redirected back.

---

## ?? **Quick Diagnostic Checklist**

Run through these checks:

- [ ] ? Build succeeded (`dotnet build`)
- [ ] ? PDF split creates files in main folder (not `/original/`)
- [ ] ? "Send Bulk Email" button exists on Split Result page
- [ ] ? Clicking button goes to `/BulkEmail/Initiate?folderId=xxx`
- [ ] ? Console logs show: "Initiating bulk email for folder: xxx"
- [ ] ? Console logs show: "Debtors found: X, Total files: Y"
- [ ] ? Preview page loads with debtor table
- [ ] ? Email input fields are visible
- [ ] ? No "Session expired" error

---

## ?? **Next Steps**

### **If Preview Page Works:**

1. **Enter email addresses** for each debtor
2. **Customize email template** (optional)
3. **Click "Send All Emails"**
4. **Expected:** SMTP authentication error (because you haven't configured SMTP yet)
5. **This is normal!** You'll see error details on the Result page

### **To Complete the Setup:**

1. **Configure SMTP credentials:**
```bash
dotnet user-secrets set "Email:Smtp:Username" "your-email@gmail.com"
dotnet user-secrets set "Email:Smtp:Password" "your-16-char-app-password"
```

2. **Test with real email:**
   - Upload PDF ? Split ? Send Bulk Email ? Enter email ? Send
   - Check your inbox for the email with PDF attachments

---

## ?? **Still Having Issues?**

### **Check Console Logs:**

Look for these specific log messages:

```
? Success logs:
- "Initiating bulk email for folder: xxx"
- "Bulk email scan completed. Debtors found: X"
- "Redirecting to Preview with session ID: xxx"
- "Loading preview for session: xxx"
- "Preview loaded successfully. Debtors: X, Files: Y"

? Error logs:
- "Bulk email initiate called without folderId"
- "No debtors found in folder: xxx"
- "Session not found: xxx"
- "Preview called without BulkSessionId in TempData"
```

### **Check File Locations:**

Verify these files exist in your project:
```
? Controllers/BulkEmailController.cs (with TempData.Peek fix)
? Views/BulkEmail/Preview.cshtml
? Views/Pdf/SplitResult.cshtml (with button)
? Services/BulkEmailService.cs
? Program.cs (services registered)
```

---

## ? **Summary**

| What Changed | Why |
|--------------|-----|
| ? `TempData.Peek()` instead of `TempData[]` | Preserves value across reads |
| ? `TempData.Keep()` added | Keeps value for POST back |
| ? `ViewBag.BulkSessionId` as backup | Alternative storage |
| ? Detailed logging added | Easier debugging |
| ? Better error messages | User-friendly guidance |

**Result:** The "Session expired" error should be **completely fixed** now! ??

---

**Try it now and let me know if you still see any issues!** ??
