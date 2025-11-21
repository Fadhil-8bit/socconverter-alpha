using System;
using System.IO;

namespace socconvertor.Services
{
    public class UploadFolderService
    {
        private const int FOLDER_EXPIRY_HOURS = 24; // Folders older than this will be cleaned up
        private readonly string _baseUploadPath;

        public UploadFolderService()
        {
            _baseUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Temp");
            Directory.CreateDirectory(_baseUploadPath);
            CleanupOldFolders();
        }

        public string CreateUploadFolder()
        {
            string folderName = DateTime.Now.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N").Substring(0, 8);
            string folderPath = Path.Combine(_baseUploadPath, folderName);
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        private void CleanupOldFolders()
        {
            try
            {
                var cutoffTime = DateTime.Now.AddHours(-FOLDER_EXPIRY_HOURS);

                foreach (var dir in Directory.GetDirectories(_baseUploadPath))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.LastAccessTime < cutoffTime)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch
                        {
                            // Ignore errors during cleanup
                        }
                    }
                }
            }
            catch
            {
                // Ignore any errors during cleanup
            }
        }
    }
}
