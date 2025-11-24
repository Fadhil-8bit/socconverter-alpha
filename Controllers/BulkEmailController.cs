using Microsoft.AspNetCore.Mvc;
using socconvertor.Services;
using socconvertor.Models.BulkEmail;
using socconvertor.Models.Email;
using Microsoft.AspNetCore.Hosting;
using System.Linq;
using System;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace socconvertor.Controllers;

public class BulkEmailController : Controller
{
    private readonly IBulkEmailService _bulkEmailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkEmailController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly UploadFolderService _uploadFolderService;

    public BulkEmailController(
        IBulkEmailService bulkEmailService,
        IConfiguration configuration,
        ILogger<BulkEmailController> logger,
        IWebHostEnvironment env,
        UploadFolderService uploadFolderService)
    {
        _bulkEmailService = bulkEmailService;
        _configuration = configuration;
        _logger = logger;
        _env = env;
        _uploadFolderService = uploadFolderService;
    }

    private static string GetDocTypeFromName(string fileName)
    {
        var upper = fileName.ToUpperInvariant();
        if (upper.Contains(" SOA ") || upper.Contains("_SOA_")) return "SOA";
        if (upper.Contains(" OD ") || upper.Contains("_OD_") || upper.Contains("OVERDUE")) return "OD";
        if (upper.Contains(" INV ") || upper.Contains("_INV_") || upper.Contains("INVOICE")) return "INV";
        return "UNKNOWN";
    }

    private List<SessionInfo> GetSessionInfos()
    {
        var list = new List<SessionInfo>();
        var tempRoot = Path.Combine(_env.WebRootPath ?? System.IO.Directory.GetCurrentDirectory(), "Temp");
        System.IO.Directory.CreateDirectory(tempRoot);
        foreach (var dir in System.IO.Directory.GetDirectories(tempRoot, "*", System.IO.SearchOption.TopDirectoryOnly))
        {
            var id = System.IO.Path.GetFileName(dir);
            if (string.IsNullOrEmpty(id)) continue;
            var pdfs = System.IO.Directory.GetFiles(dir, "*.pdf", System.IO.SearchOption.TopDirectoryOnly);
            var totalBytes = pdfs.Sum(f => new System.IO.FileInfo(f).Length);
            int soa = 0, inv = 0, od = 0, unk = 0;
            DateTime last;
            if (pdfs.Length > 0)
            {
                last = pdfs.Select(f => System.IO.File.GetLastWriteTimeUtc(f)).Max();
                foreach (var f in pdfs)
                {
                    var t = GetDocTypeFromName(System.IO.Path.GetFileName(f));
                    if (t == "SOA") soa++;
                    else if (t == "OD") od++;
                    else if (t == "INV") inv++;
                    else unk++;
                }
            }
            else
            {
                last = System.IO.Directory.GetLastWriteTimeUtc(dir);
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
                UnknownCount = unk
            });
        }
        return list
            .OrderByDescending(s => s.LastModifiedUtc)
            .ToList();
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
    [DisableRequestSizeLimit]
    // Temporarily without antiforgery while diagnosing upload binding
    public async Task<IActionResult> UploadZips(IFormFile? soaZip, IFormFile? invoiceZip, IFormFile? odZip, string? customCode)
    {
        // Fallback to raw form file collection if individual parameters did not bind
        var formFiles = Request?.Form?.Files;
        if ((soaZip == null || soaZip.Length == 0) && formFiles?.Any(f => f.Name == "soaZip") == true)
        {
            soaZip = formFiles!.First(f => f.Name == "soaZip");
        }
        if ((invoiceZip == null || invoiceZip.Length == 0) && formFiles?.Any(f => f.Name == "invoiceZip") == true)
        {
            invoiceZip = formFiles!.First(f => f.Name == "invoiceZip");
        }
        if ((odZip == null || odZip.Length == 0) && formFiles?.Any(f => f.Name == "odZip") == true)
        {
            odZip = formFiles!.First(f => f.Name == "odZip");
        }

        _logger.LogInformation("UploadZips POST: FilesCount={Count} soaZip={SoaLen} invoiceZip={InvLen} odZip={OdLen}",
            formFiles?.Count, soaZip?.Length, invoiceZip?.Length, odZip?.Length);

        // Basic presence validation
        if ((soaZip == null || soaZip.Length == 0) && (invoiceZip == null || invoiceZip.Length == 0) && (odZip == null || odZip.Length == 0))
        {
            TempData["ErrorMessage"] = "Please upload at least one ZIP file.";
            return RedirectToAction("UploadZips");
        }

        // Validate custom code (optional 4–8 digits)
        if (!string.IsNullOrEmpty(customCode) && !Regex.IsMatch(customCode, "^\\d{4,8}$"))
        {
            TempData["ErrorMessage"] = "Custom code must be 4–8 digits.";
            return RedirectToAction("UploadZips");
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var folderName = string.IsNullOrEmpty(customCode)
            ? $"{stamp}_{shortGuid}"
            : $"{stamp}_{customCode}_{shortGuid}";

        var tempRoot = Path.Combine(_env.WebRootPath ?? Directory.GetCurrentDirectory(), "Temp");
        Directory.CreateDirectory(tempRoot);
        var uploadFolderPath = Path.Combine(tempRoot, folderName);
        Directory.CreateDirectory(uploadFolderPath);

        int totalExtracted = 0;

        try
        {
            if (soaZip is { Length: > 0 })
            {
                _logger.LogInformation("Extracting SOA zip: {FileName}", soaZip.FileName);
                totalExtracted += await ExtractPdfEntriesFromZipAsync(soaZip, uploadFolderPath);
            }
            if (invoiceZip is { Length: > 0 })
            {
                _logger.LogInformation("Extracting Invoice zip: {FileName}", invoiceZip.FileName);
                totalExtracted += await ExtractPdfEntriesFromZipAsync(invoiceZip, uploadFolderPath);
            }
            if (odZip is { Length: > 0 })
            {
                _logger.LogInformation("Extracting OD zip: {FileName}", odZip.FileName);
                totalExtracted += await ExtractPdfEntriesFromZipAsync(odZip, uploadFolderPath);
            }

            if (totalExtracted == 0)
            {
                try { if (Directory.Exists(uploadFolderPath)) Directory.Delete(uploadFolderPath, true); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove empty upload folder {Folder}", uploadFolderPath); }
                TempData["ErrorMessage"] = "No PDF files found inside uploaded ZIP(s).";
                return RedirectToAction("UploadZips");
            }

            try
            {
                System.IO.File.WriteAllText(Path.Combine(uploadFolderPath, ".origin.zip"), DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to write origin marker for zip folder {Folder}", uploadFolderPath);
            }

            TempData["SuccessMessage"] = $"Created session '{folderName}' with {totalExtracted} PDF(s).";
            return RedirectToAction("InitiateManual");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting ZIP uploads to folder {Folder}", uploadFolderPath);
            try { if (Directory.Exists(uploadFolderPath)) Directory.Delete(uploadFolderPath, true); } catch { }
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
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory
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
}
