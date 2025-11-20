using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using socconvertor.Models;
using PdfReaderDemo.Models.Home;

namespace socconvertor.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IWebHostEnvironment _env;

    public HomeController(ILogger<HomeController> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public IActionResult Index()
    {
        var tempRoot = Path.Combine(_env.WebRootPath, "Temp");
        Directory.CreateDirectory(tempRoot);

        var sessionDirs = Directory.GetDirectories(tempRoot, "*", SearchOption.TopDirectoryOnly);

        var vm = new HomeDashboardViewModel();

        foreach (var dir in sessionDirs)
        {
            var id = Path.GetFileName(dir) ?? "";
            var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly);
            if (pdfs.Length == 0) continue;
            var totalBytes = pdfs.Sum(f => new System.IO.FileInfo(f).Length);
            var last = pdfs.Select(f => System.IO.File.GetLastWriteTimeUtc(f)).DefaultIfEmpty(Directory.GetLastWriteTimeUtc(dir)).Max();

            vm.TotalSessions++;
            vm.TotalPdfs += pdfs.Length;
            vm.TotalBytes += totalBytes;

            vm.RecentSessions.Add(new HomeSessionItem
            {
                Id = id,
                PdfCount = pdfs.Length,
                TotalSizeFormatted = totalBytes < 1024 ? $"{totalBytes} B" : (totalBytes < 1024 * 1024 ? $"{totalBytes / 1024.0:F1} KB" : $"{totalBytes / (1024.0 * 1024.0):F1} MB"),
                LastModifiedUtc = last
            });
        }

        vm.RecentSessions = vm.RecentSessions
            .OrderByDescending(s => s.LastModifiedUtc)
            .Take(8)
            .ToList();

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
