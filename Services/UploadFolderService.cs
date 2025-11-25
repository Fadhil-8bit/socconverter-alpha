using System;
using System.IO;

namespace socconvertor.Services
{
    public class UploadFolderService
    {
        private const int FOLDER_EXPIRY_HOURS = 24; // Folders older than this will be cleaned up
        private readonly string _baseUploadPath;

        public UploadFolderService(IStoragePaths storage)
        {
            _baseUploadPath = storage.SplitRoot; // use configured split root
            Directory.CreateDirectory(_baseUploadPath);
            CleanupOldFolders();
        }

        public string CreateUploadFolder()
        {
            string folderName = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
            string folderPath = Path.Combine(_baseUploadPath, folderName);
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        private void CleanupOldFolders()
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.AddHours(-FOLDER_EXPIRY_HOURS);
                foreach (var dir in Directory.GetDirectories(_baseUploadPath))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.LastAccessTimeUtc < cutoffTime)
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
