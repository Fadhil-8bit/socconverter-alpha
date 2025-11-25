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

namespace socconvertor.Controllers;

public class BulkEmailController : Controller
{
    private readonly IBulkEmailService _bulkEmailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkEmailController> _logger;
    private readonly IStoragePaths _paths;

    public BulkEmailController(
        IBulkEmailService bulkEmailService,
        IConfiguration configuration,
        ILogger<BulkEmailController> logger,
        IStoragePaths paths)
    {
        _bulkEmailService = bulkEmailService;
        _configuration = configuration;
        _logger = logger;
        _paths = paths;
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
            var infos = GetSessionInfos();
            model ??= new SelectSessionsViewModel();
            model.AvailableSessionIds = infos.Select(i => i.Id).ToList();
            model.Sessions = infos;
            return View(model);
        }
        try
        {
            var bulkSession = await _bulkEmailService.PrepareBulkEmailAsync(model.SelectedSessionIds);
            if (bulkSession.TotalDebtors == 0)
            {
                ModelState.AddModelError(string.Empty, "No PDF files found in the selected sessions.");
                var infos = GetSessionInfos();
                model.AvailableSessionIds = infos.Select(i => i.Id).ToList();
                model.Sessions = infos;
                return View(model);
            }
            return RedirectToAction("Preview", new { sid = bulkSession.SessionId });
        }
        catch (Exception ex)
        {
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
}
