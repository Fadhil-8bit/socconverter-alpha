using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace socconvertor.Services
{
    public interface IStoragePaths
    {
        string SplitRoot { get; }
        string BulkEmailRoot { get; }
        int BulkRetentionDays { get; }
    }

    public class StoragePaths : IStoragePaths
    {
        public string SplitRoot { get; }
        public string BulkEmailRoot { get; }
        public int BulkRetentionDays { get; }

        public StoragePaths(IConfiguration cfg, IHostEnvironment env)
        {
            var split = cfg["Storage:SplitRoot"] ?? "wwwroot/SplitSessions";
            var bulk = cfg["Storage:BulkEmailRoot"] ?? "Data/BulkEmailSessions";
            var retentionStr = cfg["Storage:BulkRetentionDays"] ?? "30";
            int.TryParse(retentionStr, out var retention);
            if (retention <= 0) retention = 30;

            // Resolve absolute paths relative to content root
            var content = env.ContentRootPath;
            SplitRoot = Path.GetFullPath(Path.Combine(content, split.Replace('/', Path.DirectorySeparatorChar)));
            BulkEmailRoot = Path.GetFullPath(Path.Combine(content, bulk.Replace('/', Path.DirectorySeparatorChar)));
            BulkRetentionDays = retention;

            Directory.CreateDirectory(SplitRoot);
            Directory.CreateDirectory(BulkEmailRoot);
        }
    }
}
