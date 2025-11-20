using Microsoft.AspNetCore.Mvc;
using PdfReaderDemo.Services;
using PdfReaderDemo.Models;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PdfReaderDemo.Controllers
{
    public class PdfController : Controller
    {
        private readonly PdfService _pdfService;
        private readonly UploadFolderService _uploadFolderService;
        private readonly ILogger<PdfController> _logger;

        public PdfController(PdfService pdfService, UploadFolderService uploadFolderService, ILogger<PdfController> logger)
        {
            _pdfService = pdfService;
            _uploadFolderService = uploadFolderService;
            _logger = logger;
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

            // Build folder path - use folderId if provided, otherwise fallback to shared Temp
            string folder;
            if (!string.IsNullOrEmpty(folderId))
            {
                var baseTemp = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
                folder = Path.Combine(baseTemp, folderId);
            }
            else
            {
                // Fallback: try TempData, then shared Temp
                var uploadFolderObj = TempData.Peek("UploadFolder");
                string? uploadFolder = uploadFolderObj as string;
                folder = !string.IsNullOrEmpty(uploadFolder)
                    ? uploadFolder
                    : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
            }

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

            string tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
            // Ensure the shared temp folder exists (we won't clear it because uploads are stored per-folder)
            Directory.CreateDirectory(tempFolder);

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
                        return View("Index");
                    }

                        splitFiles = _pdfService.SplitPdfByOd(filePath, odRecords, customCode, uploadFolder);
                }
                else
                {
                    ViewBag.Message = "Invalid split type selected.";
                    return View("Index");
                }

                // Extract folder ID (just the folder name, not full path) to pass to view
                string folderId = Path.GetFileName(uploadFolder);
                ViewBag.FolderId = folderId;
                TempData["CurrentSplitFiles"] = string.Join(";", splitFiles.Select(f => f.FileName));

                return View("SplitResult", splitFiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF upload. SplitType: {SplitType}, FileName: {FileName}", 
                    splitType, pdfFile.FileName);
                ViewBag.Message = "Error processing PDF: " + ex.Message;
                return View("Index");
            }
        }

        [HttpGet]
        public IActionResult DownloadAll(string? folderId)
        {
            // Validate folderId (only alphanumeric, underscore, hyphen - prevent path traversal)
            if (!string.IsNullOrEmpty(folderId) && !System.Text.RegularExpressions.Regex.IsMatch(folderId, @"^[a-zA-Z0-9_\-]+$"))
                return BadRequest("Invalid folder ID");

            // Build folder path
            string folder;
            if (!string.IsNullOrEmpty(folderId))
            {
                var baseTemp = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
                folder = Path.Combine(baseTemp, folderId);
            }
            else
            {
                // Fallback: try TempData
                var currentFiles = TempData["CurrentSplitFiles"]?.ToString();
                if (string.IsNullOrEmpty(currentFiles))
                    return NotFound("No files found for download.");

                var uploadFolderObj = TempData.Peek("UploadFolder");
                string? uploadFolder = uploadFolderObj as string;
                folder = !string.IsNullOrEmpty(uploadFolder)
                    ? uploadFolder
                    : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
            }

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
            if (string.IsNullOrWhiteSpace(folderId) || !Regex.IsMatch(folderId, @"^[a-zA-Z0-9_\-]+$"))
                return BadRequest("Invalid folder ID");

            var baseTemp = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
            var sessionPath = Path.Combine(baseTemp, folderId);
            if (!Directory.Exists(sessionPath))
                return NotFound("Session folder not found");

            var pdfFiles = Directory.GetFiles(sessionPath, "*.pdf", SearchOption.TopDirectoryOnly)
                .Where(f => !f.Contains(Path.Combine(sessionPath, "original"), StringComparison.OrdinalIgnoreCase))
                .ToList();

            var results = new List<SplitFileResult>();
            foreach (var fullPath in pdfFiles)
            {
                var fileName = Path.GetFileName(fullPath);
                // Attempt to infer document type from filename tokens
                var upper = fileName.ToUpperInvariant();
                DocumentType docType = DocumentType.Invoice;
                if (upper.Contains(" SOA ") || upper.Contains("_SOA_")) docType = DocumentType.SOA;
                else if (upper.Contains(" OD ") || upper.Contains("_OD_") || upper.Contains("OVERDUE")) docType = DocumentType.Overdue;
                else if (upper.Contains(" INV ") || upper.Contains("_INV_") || upper.Contains("INVOICE")) docType = DocumentType.Invoice;

                // Extract code (prefix until first space or token)
                var code = fileName.Split(' ')[0];

                results.Add(new SplitFileResult
                {
                    FileName = fileName,
                    Code = code,
                    Date = string.Empty,
                    Type = docType
                });
            }

            ViewBag.FolderId = folderId;
            return View("SplitResult", results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteSession(string folderId)
        {
            if (string.IsNullOrWhiteSpace(folderId) || !Regex.IsMatch(folderId, "^[a-zA-Z0-9_\\-]+$"))
            {
                TempData["ErrorMessage"] = "Invalid session id.";
                return RedirectToAction("Index", "Home");
            }

            try
            {
                var baseTemp = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
                var sessionPath = Path.Combine(baseTemp, folderId);
                if (!Directory.Exists(sessionPath))
                {
                    TempData["ErrorMessage"] = "Session not found.";
                }
                else
                {
                    Directory.Delete(sessionPath, true);
                    TempData["SuccessMessage"] = $"Session '{folderId}' deleted.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting session {FolderId}", folderId);
                TempData["ErrorMessage"] = $"Error deleting session: {ex.Message}";
            }

            return RedirectToAction("Index", "Home");
        }
    }
}
