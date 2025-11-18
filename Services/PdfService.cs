using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using PdfReaderDemo.Models;
using System.Text.RegularExpressions;

namespace PdfReaderDemo.Services
{
    public class PdfService
    {
        // Compiled regex patterns with timeout to prevent ReDoS attacks
        private static readonly Regex DebtorCodeRegex = new(@"Debtor Code\s*:\s*([A-Z0-9]+-[A-Z0-9]+)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex SoaDateRegex = new(@"Statement\s*of\s*Account\s*as\s*at\s*([0-9]{1,2}\s*[A-Za-z]+[.,]?\s*[0-9]{4})", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex AccountCodeRegex = new(@"Account\s*Code\s*:\s*([A-Za-z0-9]+\s*-\s*[A-Za-z0-9]+)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex InvoiceCodeRegex = new(
            @"(Debtor\s*Code|Customer\s*Code|Account\s*Code)\s*:\s*([0-9]{3,5}\s*-\s*[A-Za-z0-9]{3,})|(?:Debtor\s*Code|Customer\s*Code|Account\s*Code)\s*:\s*[\r\n]+([0-9]{3,5}\s*-\s*[A-Za-z0-9]{3,})", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex DateRegex = new(@"Date\s*:\s*([0-9]{1,2}[\/\-\.\s][0-9]{1,2}[\/\-\.\s][0-9]{2,4})", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex PageNumberRegex = new(@"(Page|Pg)\s*No*\.?\s*[:\-]?\s*(Page\s*)?([0-9]+)(\s*(of|/)\s*[0-9]+)?", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
        
        private static readonly Regex CustomCodeValidationRegex = new(@"^\d{4,8}$", 
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

        public class DebtorRecord
        {
            public string DebtorCode { get; set; } = "";
            public int PageNumber { get; set; }
        }
        public class SoaRecord
        {
            public string Date { get; set; } = "";
            public string AccountCode { get; set; } = "";
            public int PageNumber { get; set; }
        }

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
        public List<SplitFileResult> SplitPdfByAccountCode(
            string originalPdfPath,
            List<SoaRecord> records,
            string customCode = "",
            string outputFolder = "")
        {
            var outputFiles = new List<SplitFileResult>();
            var grouped = records.GroupBy(r => new { r.AccountCode, r.Date });

            // If an output folder is provided (per-upload), use it. Otherwise use shared wwwroot/Temp
            string wwwRoot = !string.IsNullOrEmpty(outputFolder) ? outputFolder : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
            Directory.CreateDirectory(wwwRoot);

            using var pdfReader = new PdfReader(originalPdfPath);
            using var pdfDoc = new PdfDocument(pdfReader);

            foreach (var group in grouped)
            {
                string accountCode = group.Key.AccountCode;
                string dateString = group.Key.Date;

                string shortCode = "0000";
                if (!string.IsNullOrEmpty(customCode) && CustomCodeValidationRegex.IsMatch(customCode))
                {
                    shortCode = customCode;
                }
                else if (DateTime.TryParse(dateString, out var parsedDate))
                {
                    shortCode = parsedDate.ToString("yyMM");
                }

                string outputFileName = $"{accountCode} SOA {shortCode}.pdf";
                string filePath = Path.Combine(wwwRoot, outputFileName);

                using var pdfWriter = new PdfWriter(filePath);
                using var newPdf = new PdfDocument(pdfWriter);

                var uniquePages = group.Select(r => r.PageNumber).Distinct().OrderBy(p => p).ToList();
                foreach (int pageNum in uniquePages)
                {
                    if (pageNum <= pdfDoc.GetNumberOfPages())
                    {
                        pdfDoc.CopyPagesTo(pageNum, pageNum, newPdf);
                    }
                }

                newPdf.Close();

                outputFiles.Add(new SplitFileResult
                {
                    FileName = outputFileName,
                    Date = dateString,
                    AccountCode = accountCode
                });
            }

            return outputFiles;
        }   
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

        public List<DebtorRecord> 
        ExtractDebtorData(string filePath)
        {
            var records = new List<DebtorRecord>();

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
                    records.Add(new DebtorRecord
                    {
                        PageNumber = i,
                        DebtorCode = currentDebtorCode
                    });
                }
            }

            return records;
        }

        public List<SplitFileResult> SplitPdfByDebtorCode(
            string originalPdfPath,
            List<DebtorRecord> records,
            string customCode = "",
            string outputFolder = "")
        {
            var outputFiles = new List<SplitFileResult>();
            var grouped = records.GroupBy(r => r.DebtorCode);
            string wwwRoot = !string.IsNullOrEmpty(outputFolder) ? outputFolder : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
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

                var uniquePages = group.Select(r => r.PageNumber).Distinct().OrderBy(p => p).ToList();
                foreach (int pageNum in uniquePages)
                {
                    if (pageNum <= pdfDoc.GetNumberOfPages())
                    {
                        pdfDoc.CopyPagesTo(pageNum, pageNum, newPdf);
                    }
                }

                newPdf.Close();

                outputFiles.Add(new SplitFileResult
                {
                    FileName = outputFileName,
                    AccountCode = debtorCode,
                    Date = DateTime.Now.ToString("yyyy-MM-dd") // Use current date since debtor split doesn't track dates
                });
            }

            return outputFiles;
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
            string wwwRoot = !string.IsNullOrEmpty(outputFolder) ? outputFolder : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
            Directory.CreateDirectory(wwwRoot);

            using var pdfReader = new PdfReader(originalPdfPath);
            using var pdfDoc = new PdfDocument(pdfReader);

            foreach (var group in grouped)
            {
                string debtorCode = group.Key.DebtorCode;
                string yearMonth = group.Key.YearMonth;

                string shortCode = "0000";
                if (!string.IsNullOrEmpty(customCode) && CustomCodeValidationRegex.IsMatch(customCode))
                {
                    shortCode = customCode;
                }
                else if (DateTime.TryParseExact(yearMonth, "yyyyMM", null, System.Globalization.DateTimeStyles.None, out var parsedYM))
                {
                    shortCode = parsedYM.ToString("yyMM");
                }

                string outputFileName = $"{debtorCode} INV {shortCode}.pdf";
                string filePath = Path.Combine(wwwRoot, outputFileName);

                using var pdfWriter = new PdfWriter(filePath);
                using var newPdf = new PdfDocument(pdfWriter);

                var uniquePages = group.Select(r => r.PageNumber).Distinct().OrderBy(p => p).ToList();
                foreach (int pageNum in uniquePages)
                {
                    if (pageNum <= pdfDoc.GetNumberOfPages())
                    {
                        pdfDoc.CopyPagesTo(pageNum, pageNum, newPdf);
                    }
                }

                newPdf.Close();

                outputFiles.Add(new SplitFileResult
                {
                    FileName = outputFileName,
                    Date = group.Max(r => r.Date) ?? DateTime.Now.ToString("yyyy-MM-dd"),
                    AccountCode = debtorCode
                });
            }

            return outputFiles;
        }
    }
}
