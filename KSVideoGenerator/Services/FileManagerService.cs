using System;
using System.IO;

namespace KSVideoGenerator.Services
{
    internal class FileManagerService
    {
        private readonly string _baseDir;

        public FileManagerService()
        {
            _baseDir = AppContext.BaseDirectory;
        }

        // assembles absolute path from relative one (supports "/" or "\")
        private string GetFullPath(string relativePath)
        {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(_baseDir, normalized);
        }

        /// <summary>
        /// Ensures the directory exists. If it does not exist, create it.
        /// </summary>
        public void EnsureDirectoryExists(string relativePath)
        {
            var path = GetFullPath(relativePath);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        /// <summary>
        /// Ensures the directory exists and removes **all** files inside it.
        /// </summary>
        public void PrepareDirectory(string relativePath)
        {
            var path = GetFullPath(relativePath);
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.GetFiles(path))
                    File.Delete(file);
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// Returns true if the file exists at the given path (relative to baseDir).
        /// </summary>
        public bool FileExists(string relativeFilePath)
        {
            var path = GetFullPath(relativeFilePath);
            return File.Exists(path);
        }
    }
}
