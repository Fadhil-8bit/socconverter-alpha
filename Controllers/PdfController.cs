using Microsoft.AspNetCore.Mvc;
using socconvertor.Services;
using socconvertor.Models;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.IO;

namespace socconvertor.Controllers;

public class PdfController : Controller
{
    private readonly PdfService _pdfService;
    private readonly UploadFolderService _uploadFolderService;
    private readonly ILogger<PdfController> _logger;
    private readonly IStoragePaths _paths;

    public PdfController(PdfService pdfService, UploadFolderService uploadFolderService, ILogger<PdfController> logger, IStoragePaths paths)
    {
        _pdfService = pdfService;
        _uploadFolderService = uploadFolderService;
        _logger = logger;
        _paths = paths;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Download(string fileName, string? folderId)
    {
        if (string.IsNullOrEmpty(fileName))
            return BadRequest();

        // Sanitize fileName to prevent path traversal
        fileName = Path.GetFileName(fileName);

        // Validate folderId (only alphanumeric, underscore, hyphen - prevent path traversal)
        if (!string.IsNullOrEmpty(folderId) && !System.Text.RegularExpressions.Regex.IsMatch(folderId, @"^[a-zA-Z0-9_\-]+$"))
            return BadRequest("Invalid folder ID");

        var baseRoot = _paths.SplitRoot;
        string folder = !string.IsNullOrEmpty(folderId) ? Path.Combine(baseRoot, folderId) : (TempData.Peek("UploadFolder") as string ?? baseRoot);

        string filePath = Path.Combine(folder, fileName);

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
        return File(fileBytes, "application/pdf", fileName);
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile pdfFile, string splitType, string customCode)
    {
        if (pdfFile == null || pdfFile.Length == 0)
        {
            ViewBag.Message = "Please upload a valid PDF file.";
            return View("Index");
        }

        // Enforce file size limit (50MB)
        const long MaxFileSize = 50 * 1024 * 1024;
        if (pdfFile.Length > MaxFileSize)
        {
            ViewBag.Message = "File too large. Maximum 50MB allowed.";
            return View("Index");
        }

        if (Path.GetExtension(pdfFile.FileName).ToLower() != ".pdf")
        {
            ViewBag.Message = "Only PDF files are allowed.";
            return View("Index");
        }

        // Validate custom code (must be 4–8 digits if provided)
        if (!string.IsNullOrEmpty(customCode) && !Regex.IsMatch(customCode, @"^\d{4,8}$"))
        {
            ViewBag.Message = "Custom code must be 4 to 8 digits.";
            return View("Index");
        }

        // create a per-upload folder and save the original PDF to an 'original' subfolder
        string uploadFolder = _uploadFolderService.CreateUploadFolder();
        string originalSubfolder = Path.Combine(uploadFolder, "original");
        Directory.CreateDirectory(originalSubfolder);
        
        var filePath = Path.Combine(originalSubfolder, Path.GetFileName(pdfFile.FileName));
        using (var stream = System.IO.File.Create(filePath))
        {
            await pdfFile.CopyToAsync(stream);
        }
        // store the upload folder for later download/zip operations
        TempData["UploadFolder"] = uploadFolder;

        try
        {
            List<SplitFileResult> splitFiles;

            if (splitType == "SOA")
            {
                var soaRecords = _pdfService.ExtractSoaData(filePath);
                if (soaRecords.Count == 0)
                {
                    ViewBag.Message = "The uploaded PDF does not contain a valid SOA format.";
                    await CleanupUploadFolderAsync(uploadFolder);
                    return View("Index");
                }

                splitFiles = _pdfService.SplitPdfBySoa(filePath, soaRecords, customCode, uploadFolder);
            }
            else if (splitType == "Invoice")
            {
                var invoiceRecords = _pdfService.ExtractInvoiceData(filePath);
                if (invoiceRecords.Count == 0)
                {
                    ViewBag.Message = "The uploaded PDF does not contain a valid Invoice format.";
                    await CleanupUploadFolderAsync(uploadFolder);
                    return View("Index");
                }

                splitFiles = _pdfService.SplitPdfByInvoice(filePath, invoiceRecords, customCode, uploadFolder);
            }
            else if (splitType == "DebtorCode")
            {
                var odRecords = _pdfService.ExtractOdData(filePath);
                if (odRecords.Count == 0)
                {
                    ViewBag.Message = "The uploaded PDF does not contain any Debtor Codes.";
                    await CleanupUploadFolderAsync(uploadFolder);
                    return View("Index");
                }

                splitFiles = _pdfService.SplitPdfByOd(filePath, odRecords, customCode, uploadFolder);
            }
            else
            {
                ViewBag.Message = "Invalid split type selected.";
                await CleanupUploadFolderAsync(uploadFolder);
                return View("Index");
            }

            // Extract folder ID (just the folder name, not full path) to pass to view
            string folderId = Path.GetFileName(uploadFolder);
            ViewBag.FolderId = folderId;
            TempData["CurrentSplitFiles"] = string.Join(";", splitFiles.Select(f => f.FileName));

            // If split produced no files (edge case), cleanup and inform user
            if (splitFiles == null || splitFiles.Count == 0)
            {
                ViewBag.Message = "No split files were produced from the uploaded PDF.";
                await CleanupUploadFolderAsync(uploadFolder);
                return View("Index");
            }

            // Optionally delete original master file after successful split
            _ = Task.Run(async () => await DeleteOriginalWithRetriesAsync(filePath, originalSubfolder));

            // After successful split, create origin marker so BulkEmail distinguishes source
            try
            {
                System.IO.File.WriteAllText(Path.Combine(uploadFolder, ".origin.split"), DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to write origin marker for split folder {Folder}", uploadFolder);
            }

            return View("SplitResult", splitFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PDF upload. SplitType: {SplitType}, FileName: {FileName}", 
                splitType, pdfFile.FileName);
            ViewBag.Message = "Error processing PDF: " + ex.Message;
            await CleanupUploadFolderAsync(uploadFolder);
            return View("Index");
        }
    }

    private async Task CleanupUploadFolderAsync(string uploadFolder)
    {
        try
        {
            // Try to delete folder immediately with a few retries
            var attempts = 3;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (Directory.Exists(uploadFolder))
                        Directory.Delete(uploadFolder, true);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(250);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(250);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup upload folder {UploadFolder}", uploadFolder);
        }
        finally
        {
            TempData.Remove("UploadFolder");
            TempData.Remove("CurrentSplitFiles");
        }
    }

    private async Task DeleteOriginalWithRetriesAsync(string filePath, string originalSubfolder)
    {
        try
        {
            var deleted = false;
            for (int attempt = 0; attempt < 3 && !deleted; attempt++)
            {
                try
                {
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);

                    if (Directory.Exists(originalSubfolder) && !Directory.EnumerateFileSystemEntries(originalSubfolder).Any())
                        Directory.Delete(originalSubfolder);

                    deleted = true;
                }
                catch (IOException)
                {
                    await Task.Delay(300);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(300);
                }
            }

            if (!deleted)
                _logger.LogWarning("Failed to remove original uploaded PDF after retries: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting original uploaded PDF: {FilePath}", filePath);
        }
    }

    [HttpGet]
    public IActionResult DownloadAll(string? folderId)
    {
        // Validate folderId (only alphanumeric, underscore, hyphen - prevent path traversal)
        if (!string.IsNullOrEmpty(folderId) && !System.Text.RegularExpressions.Regex.IsMatch(folderId, @"^[a-zA-Z0-9_\-]+$"))
            return BadRequest("Invalid folder ID");

        var root = _paths.SplitRoot;
        string folder = !string.IsNullOrEmpty(folderId) ? Path.Combine(root, folderId) : (TempData.Peek("UploadFolder") as string ?? root);

        // Check folder exists
        if (!Directory.Exists(folder))
            return NotFound($"Upload folder not found.");

        // Get PDF files from folder
        var pdfFiles = Directory.GetFiles(folder, "*.pdf");
        if (pdfFiles.Length == 0)
            return NotFound("No PDF files found in upload folder.");

        string zipFileName = "SplitFiles.zip";
        string zipPath = Path.Combine(folder, zipFileName);

        if (System.IO.File.Exists(zipPath))
            System.IO.File.Delete(zipPath);

        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var filePath in pdfFiles)
            {
                string fileName = Path.GetFileName(filePath);
                zip.CreateEntryFromFile(filePath, fileName);
            }
        }

        return PhysicalFile(zipPath, "application/zip", zipFileName);
    }

    [HttpPost]
    public async Task<IActionResult> PreviewPdf(IFormFile pdfFile)
    {
        if (pdfFile == null || pdfFile.Length == 0)
        {
            ViewBag.Message = "Please upload a valid PDF file.";
            return View("Index");
        }

        // Enforce file size limit (50MB)
        const long MaxFileSize = 50 * 1024 * 1024;
        if (pdfFile.Length > MaxFileSize)
        {
            ViewBag.Message = "File too large. Maximum 50MB allowed.";
            return View("Index");
        }

        if (Path.GetExtension(pdfFile.FileName).ToLower() != ".pdf")
        {
            ViewBag.Message = "Only PDF files are allowed.";
            return View("Index");
        }

        // Use upload folder service for preview files (will auto-cleanup after 24h)
        string previewFolder = _uploadFolderService.CreateUploadFolder();
        var filePath = Path.Combine(previewFolder, Path.GetFileName(pdfFile.FileName));
        
        using (var stream = System.IO.File.Create(filePath))
        {
            await pdfFile.CopyToAsync(stream);
        }

        try
        {
            var pageTexts = new List<string>();

            using var pdfReader = new iText.Kernel.Pdf.PdfReader(filePath);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(pdfReader);

            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                string text = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i));
                pageTexts.Add($"--- Page {i} ---\n{text}");
            }

            ViewBag.PreviewText = string.Join("\n\n", pageTexts);
            
            _logger.LogInformation("PDF preview generated successfully. File: {FileName}, Pages: {PageCount}", 
                pdfFile.FileName, pdfDoc.GetNumberOfPages());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing PDF. FileName: {FileName}", pdfFile.FileName);
            ViewBag.Message = "Error reading PDF: " + ex.Message;
            return View("Index");
        }

        return View("PreviewResult");
    }

    [HttpGet]
    public IActionResult ViewSession(string folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId) || !Regex.IsMatch(folderId, @"^[a-zA-Z0-9_\\-]+$"))
            return BadRequest("Invalid folder ID");

        var sessionPath = Path.Combine(_paths.SplitRoot, folderId);
        if (!Directory.Exists(sessionPath))
            return NotFound("Session folder not found");

        var pdfFiles = Directory.GetFiles(sessionPath, "*.pdf", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Contains(Path.Combine(sessionPath, "original"), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = new List<SplitFileResult>();
        foreach (var fullPath in pdfFiles)
        {
            var fileName = Path.GetFileName(fullPath);
            var upper = fileName.ToUpperInvariant();
            DocumentType docType = DocumentType.Unknown;
            if (upper.Contains(" SOA ") || upper.Contains("_SOA_")) docType = DocumentType.SOA;
            else if (upper.Contains(" OD ") || upper.Contains("_OD_") || upper.Contains("OVERDUE")) docType = DocumentType.Overdue;
            else if (upper.Contains(" INV ") || upper.Contains("_INV_") || upper.Contains("INVOICE")) docType = DocumentType.Invoice;
            var code = fileName.Split(' ')[0];
            results.Add(new SplitFileResult { FileName = fileName, Code = code, Date = string.Empty, Type = docType });
        }

        ViewBag.FolderId = folderId;
        return View("SplitResult", results);
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
            var sessionPath = Path.Combine(_paths.SplitRoot, folderId);
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
            _logger.LogError(ex, "Error deleting session {FolderId}", folderId);
            TempData["ErrorMessage"] = $"Error deleting session: {ex.Message}";
            return SafeRedirect(returnUrl);
        }
    }

    private IActionResult SafeRedirect(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("InitiateManual", "BulkEmail");
    }
}
