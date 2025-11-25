using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace socconvertor.Services
{
    public class BulkEmailRetentionService : BackgroundService
    {
        private readonly IStoragePaths _paths;
        private readonly ILogger<BulkEmailRetentionService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromHours(1); // scan hourly

        public BulkEmailRetentionService(IStoragePaths paths, ILogger<BulkEmailRetentionService> logger)
        {
            _paths = paths;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BulkEmailRetentionService started. RetentionDays={Days}", _paths.BulkRetentionDays);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    CleanupOldFolders();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bulk email retention cleanup failed");
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }

        private void CleanupOldFolders()
        {
            var root = _paths.BulkEmailRoot;
            Directory.CreateDirectory(root);
            var cutoff = DateTime.UtcNow.AddDays(-_paths.BulkRetentionDays);
            var dirs = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
            int deleted = 0;
            foreach (var dir in dirs)
            {
                try
                {
                    var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                    if (lastWrite < cutoff)
                    {
                        Directory.Delete(dir, true);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old bulk session {Dir}", dir);
                }
            }
            if (deleted > 0)
                _logger.LogInformation("Bulk email retention removed {Count} folders older than {Cutoff}", deleted, cutoff);
        }
    }
}
