using Microsoft.AspNetCore.Mvc;
using PdfReaderDemo.Services;
using PdfReaderDemo.Models;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace PdfReaderDemo.Controllers
{
    public class PdfController : Controller
    {
        private readonly PdfService _pdfService;
        private readonly UploadFolderService _uploadFolderService;

        public PdfController(PdfService pdfService, UploadFolderService uploadFolderService)
        {
            _pdfService = pdfService;
            _uploadFolderService = uploadFolderService;
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

            // create a per-upload folder and save the original PDF there
            string uploadFolder = _uploadFolderService.CreateUploadFolder();
            var filePath = Path.Combine(uploadFolder, Path.GetFileName(pdfFile.FileName));
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

                        splitFiles = _pdfService.SplitPdfByAccountCode(filePath, soaRecords, customCode, uploadFolder);
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
                    var debtorRecords = _pdfService.ExtractDebtorData(filePath);
                    if (debtorRecords.Count == 0)
                    {
                        ViewBag.Message = "The uploaded PDF does not contain any Debtor Codes.";
                        return View("Index");
                    }

                        splitFiles = _pdfService.SplitPdfByDebtorCode(filePath, debtorRecords, customCode, uploadFolder);
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

            byte[] zipBytes = System.IO.File.ReadAllBytes(zipPath);
            return File(zipBytes, "application/zip", zipFileName);
        }

        [HttpPost]
        public async Task<IActionResult> PreviewPdf(IFormFile pdfFile)
        {
            if (pdfFile == null || pdfFile.Length == 0)
            {
                ViewBag.Message = "Please upload a valid PDF file.";
                return View("Index");
            }

            if (Path.GetExtension(pdfFile.FileName).ToLower() != ".pdf")
            {
                ViewBag.Message = "Only PDF files are allowed.";
                return View("Index");
            }

            var filePath = Path.Combine(Path.GetTempPath(), pdfFile.FileName);
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
            }
            catch (Exception ex)
            {
                ViewBag.Message = "Error reading PDF: " + ex.Message;
                return View("Index");
            }

            return View("PreviewResult");
        }
    }
}
