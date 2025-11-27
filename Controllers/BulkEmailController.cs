using Microsoft.AspNetCore.Mvc;
using socconvertor.Services;
using socconvertor.Models.BulkEmail;
using socconvertor.Models.Email;
using Microsoft.AspNetCore.Hosting;
using System.Linq;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using socconvertor.Models.Email;

namespace socconvertor.Controllers;

public class BulkEmailController : Controller
{
    private readonly IBulkEmailService _bulkEmailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkEmailController> _logger;
    private readonly IStoragePaths _paths;
    private readonly IBulkEmailDispatchQueue _dispatchQueue;

    public BulkEmailController(
        IBulkEmailService bulkEmailService,
        IConfiguration configuration,
        ILogger<BulkEmailController> logger,
        IStoragePaths paths,
        IBulkEmailDispatchQueue dispatchQueue)
    {
        _bulkEmailService = bulkEmailService;
        _configuration = configuration;
        _logger = logger;
        _paths = paths;
        _dispatchQueue = dispatchQueue;
    }

    private List<SessionInfo> GetSessionInfos()
    {
        var list = new List<SessionInfo>();
        var root = _paths.BulkEmailRoot;
        Directory.CreateDirectory(root);
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            var id = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(id)) continue;
            var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly);
            var totalBytes = pdfs.Sum(f => new FileInfo(f).Length);
            int soa = 0, inv = 0, od = 0, unk = 0;
            DateTime last = pdfs.Length > 0 ? pdfs.Select(f => System.IO.File.GetLastWriteTimeUtc(f)).Max() : Directory.GetLastWriteTimeUtc(dir);
            foreach (var f in pdfs)
            {
                var upper = Path.GetFileName(f).ToUpperInvariant();
                if (upper.Contains(" SOA ") || upper.Contains("_SOA_")) soa++;
                else if (upper.Contains(" OD ") || upper.Contains("_OD_") || upper.Contains("OVERDUE")) od++;
                else if (upper.Contains(" INV ") || upper.Contains("_INV_") || upper.Contains("INVOICE")) inv++;
                else unk++;
            }
            list.Add(new SessionInfo
            {
                Id = id,
                PdfCount = pdfs.Length,
                TotalBytes = totalBytes,
                LastModifiedUtc = last,
                SoaCount = soa,
                InvoiceCount = inv,
                OverdueCount = od,
                UnknownCount = unk,
                Origin = "zip"
            });
        }
        return list.OrderByDescending(s => s.LastModifiedUtc).ToList();
    }

    /// <summary>
    /// Step 1 (Alternative): Manual session ID entry for multiple folders
    /// </summary>
    [HttpGet]
    public IActionResult InitiateManual()
    {
        var infos = GetSessionInfos();
        var vm = new SelectSessionsViewModel
        {
            AvailableSessionIds = infos.Select(i => i.Id).ToList(),
            Sessions = infos
        };
        return View(vm);
    }

    /// <summary>
    /// Process manual session IDs
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InitiateManual(SelectSessionsViewModel model)
    {
        if (model?.SelectedSessionIds == null || model.SelectedSessionIds.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please select at least one session.");
            var infosInitial = GetSessionInfos();
            model ??= new SelectSessionsViewModel();
            model.AvailableSessionIds = infosInitial.Select(i => i.Id).ToList();
            model.Sessions = infosInitial;
            return View(model);
        }

        // Validate that selected sessions actually contain PDFs
        var root = _paths.BulkEmailRoot;
        var existingSessions = GetSessionInfos(); // current snapshot for view refresh
        var emptySessions = new List<string>();
        var validSessions = new List<string>();
        foreach (var sid in model.SelectedSessionIds.Distinct())
        {
            var sessionPath = Path.Combine(root, sid);
            if (!Directory.Exists(sessionPath))
            {
                emptySessions.Add(sid + " (missing)");
                continue;
            }
            var pdfs = Directory.GetFiles(sessionPath, "*.pdf", SearchOption.TopDirectoryOnly);
            if (pdfs.Length == 0)
                emptySessions.Add(sid);
            else
                validSessions.Add(sid);
        }
        if (emptySessions.Count > 0)
        {
            ModelState.AddModelError(string.Empty, $"The following selected sessions have no PDF files: {string.Join(", ", emptySessions)}");
        }
        if (validSessions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Selected sessions contain no PDF files. Upload ZIPs first.");
            model.AvailableSessionIds = existingSessions.Select(i => i.Id).ToList();
            model.Sessions = existingSessions;
            return View(model);
        }
        // Use only valid sessions for grouping
        model.SelectedSessionIds = validSessions;

        _logger.LogInformation("Scan selected sessions: valid={ValidCount}, empty/missing={EmptyCount}. Valid IDs: {ValidIds}", validSessions.Count, emptySessions.Count, string.Join(",", validSessions));

        try
        {
            var bulkSession = await _bulkEmailService.PrepareBulkEmailAsync(model.SelectedSessionIds);
            _logger.LogInformation("Prepared bulk session {SessionId}. Debtors={Debtors}, Files={Files}", bulkSession.SessionId, bulkSession.TotalDebtors, bulkSession.TotalFiles);
            if (bulkSession.TotalDebtors == 0)
            {
                ModelState.AddModelError(string.Empty, "No debtor groups were produced from the selected sessions.");
                var infos = GetSessionInfos();
                model.AvailableSessionIds = infos.Select(i => i.Id).ToList();
                model.Sessions = infos;
                return View(model);
            }
            return RedirectToAction("Preview", new { sid = bulkSession.SessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrepareBulkEmailAsync failed for sessions: {ValidIds}", string.Join(",", validSessions));
            _logger.LogError(ex, "Error initiating manual bulk email with selected sessions");
            ModelState.AddModelError(string.Empty, $"Error preparing bulk email: {ex.Message}");
            var infos = GetSessionInfos();
            model!.AvailableSessionIds = infos.Select(i => i.Id).ToList();
            model.Sessions = infos;
            return View(model);
        }
    }

    /// <summary>
    /// Step 2: Preview grouped files and edit email addresses
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Preview(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            TempData["ErrorMessage"] = "Session id missing.";
            return RedirectToAction("InitiateManual");
        }
        _logger.LogInformation("Loading preview for session: {SessionId}", sid);
        var session = await _bulkEmailService.GetSessionAsync(sid);
        if (session == null)
        {
            TempData["ErrorMessage"] = $"Session not found: {sid}";
            return RedirectToAction("InitiateManual");
        }
        ViewBag.BulkSessionId = session.SessionId;
        _logger.LogInformation("Preview loaded. Debtors: {DebtorCount}, Files: {FileCount}", session.TotalDebtors, session.TotalFiles);
        _logger.LogInformation("Preview session {SessionId}: DebtorGroups={Debtors}, TotalFiles={Files}", session.SessionId, session.TotalDebtors, session.TotalFiles);
        if (session.DebtorGroups == null || session.DebtorGroups.Count == 0)
        {
            TempData["ErrorMessage"] = "No debtor groups found. Ensure selected sessions have PDFs under BulkEmailRoot.";
        }

        return View(session);
    }

    /// <summary>
    /// Step 3: Send bulk emails
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(string sessionId, List<string>? debtorCodes, List<string>? emailAddresses, string subject, string bodyTemplate)
    {
        try
        {
            debtorCodes ??= new List<string>();
            emailAddresses ??= new List<string>();
            subject ??= string.Empty;
            bodyTemplate ??= string.Empty;
            var session = await _bulkEmailService.GetSessionAsync(sessionId);
            if (session == null)
            {
                TempData["ErrorMessage"] = "Session not found";
                return RedirectToAction("InitiateManual");
            }
            var emailMappings = new Dictionary<string, string>();
            for (int i = 0; i < debtorCodes.Count && i < emailAddresses.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(debtorCodes[i]) && !string.IsNullOrWhiteSpace(emailAddresses[i]))
                    emailMappings[debtorCodes[i]] = emailAddresses[i];
            }
            if (emailMappings.Count > 0)
                await _bulkEmailService.UpdateEmailAddressesAsync(sessionId, emailMappings);
            var options = new EmailOptions
            {
                FromAddress = _configuration["Email:FromAddress"] ?? "noreply@example.com",
                FromName = _configuration["Email:FromName"] ?? "PDF Reader Demo",
                IsHtml = true,
                MaxAttachmentSizeMB = int.Parse(_configuration["Email:MaxAttachmentSizeMB"] ?? "10"),
                DelayMs = int.Parse(_configuration["Email:RateLimit:DelayMs"] ?? "0"),
                MaxPerMinute = int.Parse(_configuration["Email:RateLimit:MaxPerMinute"] ?? "0"),
                MaxPerHour = int.Parse(_configuration["Email:RateLimit:MaxPerHour"] ?? "0"),
                SmtpSettings = new SmtpSettings
                {
                    Host = _configuration["Email:Smtp:Host"] ?? "smtp.gmail.com",
                    Port = int.Parse(_configuration["Email:Smtp:Port"] ?? "587"),
                    Username = _configuration["Email:Smtp:Username"] ?? "",
                    Password = _configuration["Email:Smtp:Password"] ?? "",
                    EnableSsl = bool.Parse(_configuration["Email:Smtp:EnableSsl"] ?? "true")
                }
            };
            var result = await _bulkEmailService.SendBulkEmailsAsync(sessionId, subject, bodyTemplate, options);
            _logger.LogInformation("Bulk email send completed. Success: {SuccessCount}, Failed: {FailedCount}", result.SuccessCount, result.FailedCount);
            return View("Result", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending bulk emails");
            TempData["ErrorMessage"] = $"Error sending emails: {ex.Message}";
            return RedirectToAction("Preview", new { sid = sessionId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartQueuedSend(string sessionId, List<string>? debtorCodes, List<string>? emailAddresses, string? subject, string? bodyTemplate)
    {
        var session = await _bulkEmailService.GetSessionAsync(sessionId);
        if (session == null)
        {
            TempData["ErrorMessage"] = "Session not found";
            return RedirectToAction("InitiateManual");
        }
        debtorCodes ??= new List<string>();
        emailAddresses ??= new List<string>();
        var mappings = new Dictionary<string,string>();
        for (int i=0;i<debtorCodes.Count && i<emailAddresses.Count;i++)
            if (!string.IsNullOrWhiteSpace(debtorCodes[i]) && !string.IsNullOrWhiteSpace(emailAddresses[i]))
                mappings[debtorCodes[i]] = emailAddresses[i];
        if (mappings.Count>0) await _bulkEmailService.UpdateEmailAddressesAsync(sessionId, mappings);

        // Build job from current session debtor groups
        var job = _dispatchQueue.EnqueueFromSession(session);
        job.SubjectTemplate = string.IsNullOrWhiteSpace(subject) ? job.SubjectTemplate : subject.Trim();
        job.BodyTemplate = string.IsNullOrWhiteSpace(bodyTemplate) ? job.BodyTemplate : bodyTemplate.Trim();
        _dispatchQueue.UpdateJob(job);

        TempData["SuccessMessage"] = $"Queued job {job.JobId} with {job.Total} recipients.";
        return RedirectToAction("Progress", new { jobId = job.JobId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartQueuedSendAjax([FromBody] StartQueuedSendRequest req)
    {
        try
        {
            if (req == null || string.IsNullOrWhiteSpace(req.SessionId))
                return BadRequest(new { ok = false, error = "sessionId missing" });

            var session = await _bulkEmailService.GetSessionAsync(req.SessionId);
            if (session == null)
                return BadRequest(new { ok = false, error = "session not found" });

            // Update mappings if provided
            if (req.Mappings != null && req.Mappings.Count > 0)
                await _bulkEmailService.UpdateEmailAddressesAsync(req.SessionId, req.Mappings);

            // Create job from session
            var job = _dispatchQueue.EnqueueFromSession(session);
            job.SubjectTemplate = string.IsNullOrWhiteSpace(req.Subject) ? job.SubjectTemplate : req.Subject.Trim();
            job.BodyTemplate = string.IsNullOrWhiteSpace(req.BodyTemplate) ? job.BodyTemplate : req.BodyTemplate.Trim();
            _dispatchQueue.UpdateJob(job);

            _logger.LogInformation("Queued job {JobId} for session {SessionId} with {ItemCount} items via JSON", job.JobId, req.SessionId, job.Total);
            return Json(new { ok = true, jobId = job.JobId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in StartQueuedSendAjax for session {SessionId}", req?.SessionId);
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult Progress(string jobId)
    {
        var job = _dispatchQueue.GetJob(jobId);
        if (job == null) return NotFound();
        return View(job);
    }

    [HttpGet]
    public IActionResult ProgressJson(string jobId)
    {
        var job = _dispatchQueue.GetJob(jobId);
        if (job == null) return Json(new { ok = false, error = "Job not found" });

        var elapsedMinutes = (DateTime.UtcNow - job.CreatedUtc).TotalMinutes;
        double rate = job.SuccessCount > 0 && elapsedMinutes > 0 ? job.SuccessCount / elapsedMinutes : 0;

        int sent = job.SuccessCount;
        int failed = job.FailedCount;
        int deferred = job.DeferredCount;
        int pending = job.PendingCount;
        int total = job.Total;
        int remaining = total - (sent + failed + deferred);

        // Daily limit calculations
        int maxPerDay = int.Parse(_configuration["Email:RateLimit:MaxPerDay"] ?? "0");
        // Count items marked Sent today for this job
        int sentToday = job.Items.Count(i => i.Status == EmailDispatchItemStatus.Sent
                                             && i.LastAttemptUtc.HasValue
                                             && i.LastAttemptUtc.Value.Date == DateTime.UtcNow.Date);

        int allowedToday = maxPerDay > 0 ? Math.Max(0, maxPerDay - sentToday) : remaining;
        int willSendToday = maxPerDay > 0 ? Math.Min(remaining, allowedToday) : remaining;
        int willSendTomorrow = Math.Max(0, remaining - willSendToday);

        double etaMinutes = rate > 0 ? remaining / rate : double.NaN;

        // Get last error for diagnostics
        var lastFailedItem = job.Items
            .Where(i => i.Status == EmailDispatchItemStatus.Failed)
            .OrderByDescending(i => i.LastAttemptUtc)
            .FirstOrDefault();

        // Get currently attempting item (Sending status with attempt > 1 means retry in progress)
        var retryingItem = job.Items
            .Where(i => i.Status == EmailDispatchItemStatus.Sending && i.AttemptCount > 1)
            .OrderByDescending(i => i.LastAttemptUtc)
            .FirstOrDefault();

        return Json(new
        {
            ok = true,
            id = job.JobId,
            status = job.Status.ToString(),
            total,
            sent,
            failed,
            deferred,
            pending,
            nextResumeUtc = job.NextResumeUtc,
            completedUtc = job.CompletedUtc,
            ratePerMinute = Math.Round(rate, 2),
            etaMinutes = double.IsNaN(etaMinutes) ? (double?)null : Math.Round(etaMinutes, 1),
            remaining,
            // Daily cap visibility
            maxPerDay,
            sentToday,
            allowedToday,
            willSendToday,
            willSendTomorrow,
            // Diagnostics
            consecutiveFailures = job.ConsecutiveFailures,
            failureReason = job.FailureReason,
            lastError = lastFailedItem?.Error,
            lastErrorTime = lastFailedItem?.LastAttemptUtc,
            // Retry visibility
            currentRetry = retryingItem != null ? new
            {
                debtorCode = retryingItem.DebtorCode,
                email = retryingItem.EmailAddress,
                attemptCount = retryingItem.AttemptCount
            } : null
        });
    }

    [HttpGet]
    public IActionResult UploadZips() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadZips(IFormFile? soaZip, IFormFile? invoiceZip, IFormFile? odZip, string? customCode)
    {
        var formFiles = Request?.Form?.Files;
        if ((soaZip == null || soaZip.Length == 0) && formFiles?.Any(f => f.Name == "soaZip") == true)
            soaZip = formFiles!.First(f => f.Name == "soaZip");
        if ((invoiceZip == null || invoiceZip.Length == 0) && formFiles?.Any(f => f.Name == "invoiceZip") == true)
            invoiceZip = formFiles!.First(f => f.Name == "invoiceZip");
        if ((odZip == null || odZip.Length == 0) && formFiles?.Any(f => f.Name == "odZip") == true)
            odZip = formFiles!.First(f => f.Name == "odZip");

        if ((soaZip == null || soaZip.Length == 0) && (invoiceZip == null || invoiceZip.Length == 0) && (odZip == null || odZip.Length == 0))
        {
            TempData["ErrorMessage"] = "Please upload at least one ZIP file.";
            return RedirectToAction("UploadZips");
        }
        if (!string.IsNullOrEmpty(customCode) && !Regex.IsMatch(customCode, "^\\d{4,8}$"))
        {
            TempData["ErrorMessage"] = "Custom code must be 4–8 digits.";
            return RedirectToAction("UploadZips");
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var folderName = string.IsNullOrEmpty(customCode) ? $"{stamp}_{shortGuid}" : $"{stamp}_{customCode}_{shortGuid}";
        var root = _paths.BulkEmailRoot;
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, folderName);
        Directory.CreateDirectory(sessionPath);

        int totalExtracted = 0;
        try
        {
            if (soaZip is { Length: > 0 }) totalExtracted += await ExtractPdfEntriesFromZipAsync(soaZip, sessionPath);
            if (invoiceZip is { Length: > 0 }) totalExtracted += await ExtractPdfEntriesFromZipAsync(invoiceZip, sessionPath);
            if (odZip is { Length: > 0 }) totalExtracted += await ExtractPdfEntriesFromZipAsync(odZip, sessionPath);
            if (totalExtracted == 0)
            {
                try { Directory.Delete(sessionPath, true); } catch { }
                TempData["ErrorMessage"] = "No PDF files found inside uploaded ZIP(s).";
                return RedirectToAction("UploadZips");
            }
            TempData["SuccessMessage"] = $"Created session '{folderName}' with {totalExtracted} PDF(s).";
            return RedirectToAction("InitiateManual");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting ZIP uploads to folder {Folder}", sessionPath);
            try { if (Directory.Exists(sessionPath)) Directory.Delete(sessionPath, true); } catch { }
            TempData["ErrorMessage"] = "Error processing ZIP(s): " + ex.Message;
            return RedirectToAction("UploadZips");
        }
    }

    private static async Task<int> ExtractPdfEntriesFromZipAsync(IFormFile zipFile, string destinationFolder)
    {
        int extracted = 0;
        using var zipStream = zipFile.OpenReadStream();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var ext = Path.GetExtension(entry.Name);
            if (!ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = Path.GetFileName(entry.Name);
            if (string.IsNullOrEmpty(fileName)) continue;
            var destPath = Path.Combine(destinationFolder, fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            int suffix = 1;
            while (System.IO.File.Exists(destPath))
            {
                destPath = Path.Combine(destinationFolder, $"{baseName}_{suffix}.pdf");
                suffix++;
            }
            Directory.CreateDirectory(destinationFolder);
            using var entryStream = entry.Open();
            using var outStream = System.IO.File.Create(destPath);
            await entryStream.CopyToAsync(outStream);
            extracted++;
        }
        return extracted;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteSession(string folderId, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(folderId) || !Regex.IsMatch(folderId, @"^[a-zA-Z0-9_\-]+$"))
        {
            TempData["ErrorMessage"] = "Invalid session id.";
            return SafeRedirect(returnUrl);
        }
        try
        {
            var sessionPath = Path.Combine(_paths.BulkEmailRoot, folderId);
            if (!Directory.Exists(sessionPath))
            {
                TempData["ErrorMessage"] = "Session not found.";
                return SafeRedirect(returnUrl);
            }
            Directory.Delete(sessionPath, true);
            TempData["SuccessMessage"] = $"Session '{folderId}' deleted.";
            return SafeRedirect(returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting bulk session {FolderId}", folderId);
            TempData["ErrorMessage"] = $"Error deleting session: {ex.Message}";
            return SafeRedirect(returnUrl);
        }
    }

    private IActionResult SafeRedirect(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("InitiateManual");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadZipsAjax(IFormFile? soaZip, IFormFile? invoiceZip, IFormFile? odZip, string? customCode)
    {
        try
        {
            if ((soaZip == null || soaZip.Length == 0) && (invoiceZip == null || invoiceZip.Length == 0) && (odZip == null || odZip.Length == 0))
                return Json(new { ok = false, error = "Please provide at least one ZIP file." });
            if (!string.IsNullOrEmpty(customCode) && !Regex.IsMatch(customCode, "^\\d{4,8}$"))
                return Json(new { ok = false, error = "Custom code must be 4–8 digits." });

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var shortGuid = Guid.NewGuid().ToString("N")[..8];
            var folderName = string.IsNullOrEmpty(customCode) ? $"{stamp}_{shortGuid}" : $"{stamp}_{customCode}_{shortGuid}";
            var root = _paths.BulkEmailRoot;
            Directory.CreateDirectory(root);
            var sessionPath = Path.Combine(root, folderName);
            Directory.CreateDirectory(sessionPath);

            int totalExtracted = 0;
            int soaCount = 0, invCount = 0, odCount = 0, unk = 0;

            async Task<int> Extract(IFormFile zf)
            {
                if (zf is not { Length: > 0 }) return 0;
                int extracted = 0;
                using var zipStream = zf.OpenReadStream();
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    if (!Path.GetExtension(entry.Name).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    var fileName = Path.GetFileName(entry.Name);
                    var destPath = Path.Combine(sessionPath, fileName);
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    int suffix = 1;
                    while (System.IO.File.Exists(destPath))
                    {
                        destPath = Path.Combine(sessionPath, $"{baseName}_{suffix}.pdf");
                        suffix++;
                    }
                    using var entryStream = entry.Open();
                    using var outStream = System.IO.File.Create(destPath);
                    await entryStream.CopyToAsync(outStream);
                    extracted++;
                    var upper = fileName.ToUpperInvariant();
                    if (upper.Contains("SOA")) soaCount++;
                    else if (upper.Contains("INV") || upper.Contains("INVOICE")) invCount++;
                    else if (upper.Contains("OD") || upper.Contains("OVERDUE")) odCount++;
                    else unk++;
                }
                return extracted;
            }

            totalExtracted += await Extract(soaZip);
            totalExtracted += await Extract(invoiceZip);
            totalExtracted += await Extract(odZip);

            if (totalExtracted == 0)
            {
                try { Directory.Delete(sessionPath, true); } catch { }
                return Json(new { ok = false, error = "No PDF files found in provided ZIP(s)." });
            }

            return Json(new { ok = true, sessionId = folderName, total = totalExtracted, soa = soaCount, inv = invCount, od = odCount, unk });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadZipsAjax error");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CancelJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return RedirectToAction("InitiateManual");
        _dispatchQueue.CancelJob(jobId);
        TempData["SuccessMessage"] = $"Job {jobId} cancelled.";
        return RedirectToAction("Progress", new { jobId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ClearJobs(bool onlyFinished = true)
    {
        var removed = _dispatchQueue.ClearJobs(onlyFinished);
        TempData["SuccessMessage"] = $"Removed {removed} job(s).";
        return RedirectToAction("Index", "Home");
    }

    /// <summary>
    /// Save debtor→email mappings via JSON to avoid large form field posts.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMappingsAjax([FromBody] Dictionary<string,string> mappings, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest(new { ok=false, error="sessionId missing" });
        var session = await _bulkEmailService.GetSessionAsync(sessionId);
        if (session == null) return BadRequest(new { ok=false, error="session not found" });
        await _bulkEmailService.UpdateEmailAddressesAsync(sessionId, mappings);
        return Json(new { ok=true, count=mappings.Count });
    }

    [HttpGet]
    public IActionResult JobItemsJson(string jobId, int page = 1, int pageSize = 100, string? status = null)
    {
        var job = _dispatchQueue.GetJob(jobId);
        if (job == null) return Json(new { ok = false, error = "Job not found" });

        var query = job.Items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<EmailDispatchItemStatus>(status, true, out var statusEnum))
                query = query.Where(i => i.Status == statusEnum);
        }

        var total = query.Count();
        // replace the projection in JobItemsJson to serialize lastAttemptUtc as ISO-8601 string
        var items = query
            .OrderBy(i => i.DebtorCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                debtorCode = i.DebtorCode,
                email = i.EmailAddress,
                status = i.Status.ToString(),
                lastAttemptUtc = i.LastAttemptUtc.HasValue ? i.LastAttemptUtc.Value.ToString("o") : null,
                error = i.Error,
                attemptCount = i.AttemptCount
            })
            .ToList();

        return Json(new { ok = true, jobId = job.JobId, total, page, pageSize, items });
    }
}
