using Microsoft.AspNetCore.Mvc;
using PdfReaderDemo.Services;
using PdfReaderDemo.Models.BulkEmail;
using PdfReaderDemo.Models.Email;

namespace PdfReaderDemo.Controllers;

public class BulkEmailController : Controller
{
    private readonly IBulkEmailService _bulkEmailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkEmailController> _logger;

    public BulkEmailController(
        IBulkEmailService bulkEmailService,
        IConfiguration configuration,
        ILogger<BulkEmailController> logger)
    {
        _bulkEmailService = bulkEmailService;
        _configuration = configuration;
        _logger = logger;
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

            // Store session ID in TempData
            TempData["BulkSessionId"] = bulkSession.SessionId;

            _logger.LogInformation("Redirecting to Preview with session ID: {SessionId}", bulkSession.SessionId);
            return RedirectToAction("Preview");
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
        return View();
    }

    /// <summary>
    /// Process manual session IDs
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> InitiateManual(string sessionIds)
    {
        if (string.IsNullOrWhiteSpace(sessionIds))
        {
            ViewBag.ErrorMessage = "Please enter at least one session ID";
            return View();
        }

        try
        {
            // Parse session IDs (comma or newline separated)
            var sessionIdList = sessionIds
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (sessionIdList.Count == 0)
            {
                ViewBag.ErrorMessage = "No valid session IDs found";
                return View();
            }

            // Prepare bulk email session
            var bulkSession = await _bulkEmailService.PrepareBulkEmailAsync(sessionIdList);

            if (bulkSession.TotalDebtors == 0)
            {
                ViewBag.ErrorMessage = "No PDF files found in the specified session folders";
                return View();
            }

            // Store session ID in TempData
            TempData["BulkSessionId"] = bulkSession.SessionId;

            return RedirectToAction("Preview");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating manual bulk email");
            ViewBag.ErrorMessage = $"Error preparing bulk email: {ex.Message}";
            return View();
        }
    }

    /// <summary>
    /// Step 2: Preview grouped files and edit email addresses
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Preview()
    {
        // Try to get session ID from TempData first
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

        // Keep TempData for potential POST back
        TempData.Keep("BulkSessionId");
        
        // Also pass via ViewBag as backup
        ViewBag.BulkSessionId = sessionId;

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
            return RedirectToAction("Preview");
        }
    }
}
