using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using PdfReaderDemo.Models;
using System.Text.RegularExpressions;

namespace PdfReaderDemo.Services
{
    public class PdfService
    {
        #region Regex Patterns
        
        // Compiled regex patterns with timeout to prevent ReDoS attacks
        
        // Common validation patterns
        private static readonly Regex CustomCodeValidationRegex = new(@"^\d{4,8}$", 
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        // SOA-specific patterns
        private static readonly Regex SoaDateRegex = new(@"Statement\s*of\s*Account\s*as\s*at\s*([0-9]{1,2}\s*[A-Za-z]+[.,]?\s*[0-9]{4})", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex AccountCodeRegex = new(@"Account\s*Code\s*:\s*([A-Za-z0-9]+\s*-\s*[A-Za-z0-9]+)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        // Invoice-specific patterns
        private static readonly Regex InvoiceCodeRegex = new(
            @"(Debtor\s*Code|Customer\s*Code|Account\s*Code)\s*:\s*([0-9]{3,5}\s*-\s*[A-Za-z0-9]{3,})|(?:Debtor\s*Code|Customer\s*Code|Account\s*Code)\s*:\s*[\r\n]+([0-9]{3,5}\s*-\s*[A-Za-z0-9]{3,})", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex DateRegex = new(@"Date\s*:\s*([0-9]{1,2}[\/\-\.\s][0-9]{1,2}[\/\-\.\s][0-9]{2,4})", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex PageNumberRegex = new(@"(Page|Pg)\s*No*\.?\s*[:\-]?\s*(Page\s*)?([0-9]+)(\s*(of|/)\s*[0-9]+)?", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        // OD (Overdue) specific patterns
        private static readonly Regex DebtorCodeRegex = new(@"Debtor Code\s*:\s*([A-Z0-9]+-[A-Z0-9]+)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        #endregion

        #region SOA Operations

        public List<SoaRecord> ExtractSoaData(string filePath)
        {
            var records = new List<SoaRecord>();

            using var pdfReader = new PdfReader(filePath);
            using var pdfDoc = new PdfDocument(pdfReader);

            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                string pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i));

                var dateMatch = SoaDateRegex.Match(pageText);
                var codeMatch = AccountCodeRegex.Match(pageText);

                if (dateMatch.Success && codeMatch.Success)
                {
                    string code = codeMatch.Groups[1].Value.Replace(" ", "").Trim();

                    string dateNorm;
                    if (DateTime.TryParse(dateMatch.Groups[1].Value, out DateTime parsedDate))
                        dateNorm = parsedDate.ToString("yyyy-MM-dd");
                    else
                        dateNorm = dateMatch.Groups[1].Value.Trim();

                    if (!records.Any(r => r.PageNumber == i && r.AccountCode == code && r.Date == dateNorm))
                    {
                        records.Add(new SoaRecord
                        {
                            PageNumber = i,
                            AccountCode = code,
                            Date = dateNorm
                        });
                    }
                }
            }

            return records;
        }

        public List<SplitFileResult> SplitPdfBySoa(
            string originalPdfPath,
            List<SoaRecord> records,
            string customCode = "",
            string outputFolder = "")
        {
            var outputFiles = new List<SplitFileResult>();
            var grouped = records.GroupBy(r => new { r.AccountCode, r.Date });

            string wwwRoot = GetOutputFolder(outputFolder);
            Directory.CreateDirectory(wwwRoot);

            using var pdfReader = new PdfReader(originalPdfPath);
            using var pdfDoc = new PdfDocument(pdfReader);

            foreach (var group in grouped)
            {
                string accountCode = group.Key.AccountCode;
                string dateString = group.Key.Date;

                string shortCode = GetShortCode(customCode, dateString);
                string outputFileName = $"{accountCode} SOA {shortCode}.pdf";
                string filePath = Path.Combine(wwwRoot, outputFileName);

                using var pdfWriter = new PdfWriter(filePath);
                using var newPdf = new PdfDocument(pdfWriter);

                var pageNumbers = group.Select(r => r.PageNumber).ToList();
                CopyPagesToNewPdf(pdfDoc, newPdf, pageNumbers);

                newPdf.Close();

                outputFiles.Add(new SplitFileResult
                {
                    FileName = outputFileName,
                    Date = dateString,
                    Code = accountCode,
                    Type = DocumentType.SOA
                });
            }

            return outputFiles;
        }

        #endregion

        #region Invoice Operations

        public List<InvoiceRecord> ExtractInvoiceData(string filePath)
        {
            var records = new List<InvoiceRecord>();

            using var pdfReader = new PdfReader(filePath);
            using var pdfDoc = new PdfDocument(pdfReader);

            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                string pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i));

                var codeMatch = InvoiceCodeRegex.Match(pageText);

                if (!codeMatch.Success) continue;

                string debtorCode = "";
                if (codeMatch.Groups[2].Success)
                    debtorCode = codeMatch.Groups[2].Value.Replace(" ", "").Trim();
                else if (codeMatch.Groups[3].Success)
                    debtorCode = codeMatch.Groups[3].Value.Replace(" ", "").Trim();

                var dateMatch = DateRegex.Match(pageText);

                string dateNorm = "";
                if (dateMatch.Success)
                {
                    if (DateTime.TryParse(dateMatch.Groups[1].Value, out DateTime parsedDate))
                        dateNorm = parsedDate.ToString("yyyy-MM-dd");
                    else
                        dateNorm = dateMatch.Groups[1].Value.Trim();
                }

                var pageMatch = PageNumberRegex.Match(pageText);

                if (!pageMatch.Success) continue;

                int currentPage = int.Parse(pageMatch.Groups[3].Value);

                if (!records.Any(r => r.PageNumber == i && r.DebtorCode == debtorCode && r.Date == dateNorm))
                {
                    records.Add(new InvoiceRecord
                    {
                        PageNumber = i,
                        DebtorCode = debtorCode,
                        Date = dateNorm
                    });
                }
            }

            return records;
        }

        public List<SplitFileResult> SplitPdfByInvoice(
            string originalPdfPath,
            List<InvoiceRecord> records,
            string customCode = "",
            string outputFolder = "")
        {
            var outputFiles = new List<SplitFileResult>();
            var grouped = records.GroupBy(r =>
            {
                if (DateTime.TryParse(r.Date, out var parsed))
                    return new { r.DebtorCode, YearMonth = parsed.ToString("yyyyMM") };
                else
                    return new { r.DebtorCode, YearMonth = "0000" };
            });

            string wwwRoot = GetOutputFolder(outputFolder);
            Directory.CreateDirectory(wwwRoot);

            using var pdfReader = new PdfReader(originalPdfPath);
            using var pdfDoc = new PdfDocument(pdfReader);

            foreach (var group in grouped)
            {
                string debtorCode = group.Key.DebtorCode;
                string yearMonth = group.Key.YearMonth;

                string shortCode = GetShortCode(customCode, yearMonth, isYearMonth: true);
                string outputFileName = $"{debtorCode} INV {shortCode}.pdf";
                string filePath = Path.Combine(wwwRoot, outputFileName);

                using var pdfWriter = new PdfWriter(filePath);
                using var newPdf = new PdfDocument(pdfWriter);

                var pageNumbers = group.Select(r => r.PageNumber).ToList();
                CopyPagesToNewPdf(pdfDoc, newPdf, pageNumbers);

                newPdf.Close();

                outputFiles.Add(new SplitFileResult
                {
                    FileName = outputFileName,
                    Date = group.Max(r => r.Date) ?? DateTime.Now.ToString("yyyy-MM-dd"),
                    Code = debtorCode,
                    Type = DocumentType.Invoice
                });
            }

            return outputFiles;
        }

        #endregion

        #region OD (Overdue) Operations

        public List<OdRecord> ExtractOdData(string filePath)
        {
            var records = new List<OdRecord>();

            using var pdfReader = new PdfReader(filePath);
            using var pdfDoc = new PdfDocument(pdfReader);

            string currentDebtorCode = "UNCLASSIFIED";

            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                string pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i));

                var debtorMatch = DebtorCodeRegex.Match(pageText);
                if (debtorMatch.Success)
                {
                    currentDebtorCode = debtorMatch.Groups[1].Value.Trim();
                }

                // Always add a record with the current debtor code
                if (!records.Any(r => r.PageNumber == i && r.DebtorCode == currentDebtorCode))
                {
                    records.Add(new OdRecord
                    {
                        PageNumber = i,
                        DebtorCode = currentDebtorCode
                    });
                }
            }

            return records;
        }

        public List<SplitFileResult> SplitPdfByOd(
            string originalPdfPath,
            List<OdRecord> records,
            string customCode = "",
            string outputFolder = "")
        {
            var outputFiles = new List<SplitFileResult>();
            var grouped = records.GroupBy(r => r.DebtorCode);

            string wwwRoot = GetOutputFolder(outputFolder);
            Directory.CreateDirectory(wwwRoot);

            using var pdfReader = new PdfReader(originalPdfPath);
            using var pdfDoc = new PdfDocument(pdfReader);

            foreach (var group in grouped)
            {
                string debtorCode = group.Key;

                // Always include the literal "OD" token. If a numeric customCode is provided,
                // append it after OD, separated by spaces. Examples:
                //   "DEBTORCODE OD.pdf"
                //   "DEBTORCODE OD 1234.pdf"
                string odToken = "OD";
                string sanitizedCustom = "";
                if (!string.IsNullOrEmpty(customCode) && CustomCodeValidationRegex.IsMatch(customCode))
                {
                    sanitizedCustom = customCode.Trim();
                }

                string outputFileName = string.IsNullOrEmpty(sanitizedCustom)
                    ? $"{debtorCode} {odToken}.pdf"
                    : $"{debtorCode} {odToken} {sanitizedCustom}.pdf";
                string filePath = Path.Combine(wwwRoot, outputFileName);

                using var pdfWriter = new PdfWriter(filePath);
                using var newPdf = new PdfDocument(pdfWriter);

                var pageNumbers = group.Select(r => r.PageNumber).ToList();
                CopyPagesToNewPdf(pdfDoc, newPdf, pageNumbers);

                newPdf.Close();

                outputFiles.Add(new SplitFileResult
                {
                    FileName = outputFileName,
                    Code = debtorCode,
                    Date = DateTime.Now.ToString("yyyy-MM-dd"), // Use current date since OD split doesn't track dates
                    Type = DocumentType.Overdue
                });
            }

            return outputFiles;
        }

        #endregion

        #region Common Helpers

        private static string GetOutputFolder(string outputFolder)
        {
            return !string.IsNullOrEmpty(outputFolder)
                ? outputFolder
                : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
        }

        private static string GetShortCode(string customCode, string fallbackValue, bool isYearMonth = false)
        {
            if (!string.IsNullOrEmpty(customCode) && CustomCodeValidationRegex.IsMatch(customCode))
                return customCode;

            if (isYearMonth)
            {
                if (DateTime.TryParseExact(fallbackValue, "yyyyMM", null, System.Globalization.DateTimeStyles.None, out var parsedYM))
                    return parsedYM.ToString("yyMM");
            }
            else
            {
                if (DateTime.TryParse(fallbackValue, out var parsed))
                    return parsed.ToString("yyMM");
            }

            return "0000";
        }

        private static void CopyPagesToNewPdf(PdfDocument sourcePdf, PdfDocument targetPdf, List<int> pageNumbers)
        {
            var uniquePages = pageNumbers.Distinct().OrderBy(p => p).ToList();
            foreach (int pageNum in uniquePages)
            {
                if (pageNum <= sourcePdf.GetNumberOfPages())
                {
                    sourcePdf.CopyPagesTo(pageNum, pageNum, targetPdf);
                }
            }
        }

        #endregion
    }
}
