using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using socconvertor.Models;
using socconvertor.Models.Home;
using socconvertor.Helpers;
using socconvertor.Services;
using socconvertor.Models.Email;

namespace socconvertor.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IStoragePaths _paths;
    private readonly IBulkEmailDispatchQueue _dispatchQueue;

    public HomeController(ILogger<HomeController> logger, IStoragePaths paths, IBulkEmailDispatchQueue dispatchQueue)
    {
        _logger = logger;
        _paths = paths;
        _dispatchQueue = dispatchQueue;
    }

    public IActionResult Index()
    {
        var vm = new HomeDashboardViewModel();

        // Split sessions (inside webroot)
        var splitRoot = _paths.SplitRoot;
        Directory.CreateDirectory(splitRoot);
        foreach (var dir in Directory.GetDirectories(splitRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var id = Path.GetFileName(dir) ?? "";
            var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly);
            if (pdfs.Length == 0) continue;
            var totalBytes = pdfs.Sum(f => new System.IO.FileInfo(f).Length);
            var last = pdfs.Select(f => System.IO.File.GetLastWriteTimeUtc(f)).DefaultIfEmpty(Directory.GetLastWriteTimeUtc(dir)).Max();

            vm.SplitSessionsCount++;
            vm.SplitPdfCount += pdfs.Length;
            vm.TotalBytes += totalBytes;

            vm.RecentActivities.Add(new HomeSessionItem
            {
                Id = id,
                PdfCount = pdfs.Length,
                TotalSizeFormatted = FormatHelpers.FormatBytes(totalBytes),
                LastModifiedUtc = last,
                Origin = "split"
            });
        }

        // Bulk email sessions (outside webroot)
        var bulkRoot = _paths.BulkEmailRoot;
        Directory.CreateDirectory(bulkRoot);
        foreach (var dir in Directory.GetDirectories(bulkRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var id = Path.GetFileName(dir) ?? "";
            var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly);
            if (pdfs.Length == 0) continue;
            var totalBytes = pdfs.Sum(f => new System.IO.FileInfo(f).Length);
            var last = pdfs.Select(f => System.IO.File.GetLastWriteTimeUtc(f)).DefaultIfEmpty(Directory.GetLastWriteTimeUtc(dir)).Max();

            vm.BulkSessionsCount++;
            vm.BulkPdfCount += pdfs.Length;
            vm.TotalBytes += totalBytes;

            vm.RecentActivities.Add(new HomeSessionItem
            {
                Id = id,
                PdfCount = pdfs.Length,
                TotalSizeFormatted = FormatHelpers.FormatBytes(totalBytes),
                LastModifiedUtc = last,
                Origin = "zip"
            });
        }

        vm.RecentActivities = vm.RecentActivities
            .OrderByDescending(s => s.LastModifiedUtc)
            .Take(8)
            .ToList();

        vm.Jobs = _dispatchQueue.GetAllJobs().OrderByDescending(j => j.CreatedUtc).Take(10).ToList();

        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
