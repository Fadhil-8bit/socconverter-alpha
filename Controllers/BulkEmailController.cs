using Microsoft.AspNetCore.Mvc;
using PdfReaderDemo.Services;
using PdfReaderDemo.Models.BulkEmail;
using PdfReaderDemo.Models.Email;
using Microsoft.AspNetCore.Hosting;

namespace PdfReaderDemo.Controllers;

public class BulkEmailController : Controller
{
    private readonly IBulkEmailService _bulkEmailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkEmailController> _logger;
    private readonly IWebHostEnvironment _env;

    public BulkEmailController(
        IBulkEmailService bulkEmailService,
        IConfiguration configuration,
        ILogger<BulkEmailController> logger,
        IWebHostEnvironment env)
    {
        _bulkEmailService = bulkEmailService;
        _configuration = configuration;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Step 1: Initiate bulk email from a single session folder (called from SplitResult page)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Initiate(string? folderId)
    {
        if (string.IsNullOrEmpty(folderId))
        {
            TempData["ErrorMessage"] = "No folder ID provided";
            _logger.LogWarning("Bulk email initiate called without folderId");
            return RedirectToAction("Index", "Pdf");
        }

        try
        {
            _logger.LogInformation("Initiating bulk email for folder: {FolderId}", folderId);
            
            // Prepare bulk email session from single folder
            var sessionIds = new List<string> { folderId };
            var bulkSession = await _bulkEmailService.PrepareBulkEmailAsync(sessionIds);

            _logger.LogInformation("Bulk email scan completed. Debtors found: {DebtorCount}, Total files: {FileCount}", 
                bulkSession.TotalDebtors, bulkSession.TotalFiles);

            if (bulkSession.TotalDebtors == 0)
            {
                TempData["ErrorMessage"] = $"No PDF files found in session folder '{folderId}'. Make sure PDFs are in the main folder, not in /original/ subfolder.";
                _logger.LogWarning("No debtors found in folder: {FolderId}", folderId);
                return RedirectToAction("Index", "Pdf");
            }

            // Store session ID for next request (best-effort)
            TempData["BulkSessionId"] = bulkSession.SessionId;

            _logger.LogInformation("Redirecting to Preview with session ID: {SessionId}", bulkSession.SessionId);
            // Also pass sid via query to avoid TempData dependency
            return RedirectToAction("Preview", new { sid = bulkSession.SessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating bulk email for folder: {FolderId}", folderId);
            TempData["ErrorMessage"] = $"Error preparing bulk email: {ex.Message}";
            return RedirectToAction("Index", "Pdf");
        }
    }

    /// <summary>
    /// Step 1 (Alternative): Manual session ID entry for multiple folders
    /// </summary>
    [HttpGet]
    public IActionResult InitiateManual()
    {
        // Enumerate session folders under wwwroot/Temp
        var tempRoot = Path.Combine(_env.WebRootPath, "Temp");
        Directory.CreateDirectory(tempRoot);
        var sessionIds = Directory
            .GetDirectories(tempRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderByDescending(id => id)
            .ToList()!;

        var vm = new SelectSessionsViewModel
        {
            AvailableSessionIds = sessionIds
        };

        return View(vm);
    }

    /// <summary>
    /// Process manual session IDs
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> InitiateManual(SelectSessionsViewModel model)
    {
        if (model?.SelectedSessionIds == null || model.SelectedSessionIds.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please select at least one session.");
            // Rebuild available sessions
            var tempRoot = Path.Combine(_env.WebRootPath, "Temp");
            Directory.CreateDirectory(tempRoot);
            model ??= new SelectSessionsViewModel();
            model.AvailableSessionIds = Directory
                .GetDirectories(tempRoot, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(id => !string.IsNullOrEmpty(id))
                .OrderByDescending(id => id)
                .ToList()!;
            return View(model);
        }

        try
        {
            var bulkSession = await _bulkEmailService.PrepareBulkEmailAsync(model.SelectedSessionIds);
            if (bulkSession.TotalDebtors == 0)
            {
                ModelState.AddModelError(string.Empty, "No PDF files found in the selected sessions.");
                // reload available list
                var tempRoot = Path.Combine(_env.WebRootPath, "Temp");
                model.AvailableSessionIds = Directory
                    .GetDirectories(tempRoot, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .OrderByDescending(id => id)
                    .ToList()!;
                return View(model);
            }

            TempData["BulkSessionId"] = bulkSession.SessionId;
            return RedirectToAction("Preview", new { sid = bulkSession.SessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating manual bulk email with selected sessions");
            ModelState.AddModelError(string.Empty, $"Error preparing bulk email: {ex.Message}");
            // reload available list
            var tempRoot = Path.Combine(_env.WebRootPath, "Temp");
            model.AvailableSessionIds = Directory
                .GetDirectories(tempRoot, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(id => !string.IsNullOrEmpty(id))
                .OrderByDescending(id => id)
                .ToList()!;
            return View(model);
        }
    }

    /// <summary>
    /// Step 2: Preview grouped files and edit email addresses
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Preview(string? sid = null)
    {
        string? sessionId = sid;

        // If sid not provided, fall back to TempData
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = TempData.Peek("BulkSessionId") as string;
        }

        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogWarning("Preview called without BulkSessionId in query or TempData");
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

        // Keep TempData and also refresh it from sid for subsequent requests
        TempData["BulkSessionId"] = session.SessionId;
        TempData.Keep("BulkSessionId");
        
        // Also pass via ViewBag as backup
        ViewBag.BulkSessionId = session.SessionId;

        _logger.LogInformation("Preview loaded successfully. Debtors: {DebtorCount}, Files: {FileCount}", 
            session.TotalDebtors, session.TotalFiles);

        return View(session);
    }

    /// <summary>
    /// Step 3: Send bulk emails
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Send(
        string sessionId,
        List<string> debtorCodes,
        List<string> emailAddresses,
        string subject,
        string bodyTemplate)
    {
        try
        {
            // Load session
            var session = await _bulkEmailService.GetSessionAsync(sessionId);

            if (session == null)
            {
                TempData["ErrorMessage"] = "Session not found";
                return RedirectToAction("Index", "Pdf");
            }

            // Update email addresses
            var emailMappings = new Dictionary<string, string>();
            for (int i = 0; i < debtorCodes.Count && i < emailAddresses.Count; i++)
            {
                emailMappings[debtorCodes[i]] = emailAddresses[i];
            }

            await _bulkEmailService.UpdateEmailAddressesAsync(sessionId, emailMappings);

            // Prepare email options
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

            // Send emails
            var result = await _bulkEmailService.SendBulkEmailsAsync(sessionId, subject, bodyTemplate, options);

            _logger.LogInformation("Bulk email send completed. Success: {SuccessCount}, Failed: {FailedCount}",
                result.SuccessCount, result.FailedCount);

            return View("Result", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending bulk emails");
            TempData["ErrorMessage"] = $"Error sending emails: {ex.Message}";

            // Reload session for display
            TempData["BulkSessionId"] = sessionId;
            return RedirectToAction("Preview", new { sid = sessionId });
        }
    }
}
