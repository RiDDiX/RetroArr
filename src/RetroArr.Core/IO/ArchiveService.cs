using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using System.Collections.Generic;

namespace RetroArr.Core.IO
{
    public interface IArchiveService
    {
        bool Extract(string sourceFile, string destinationDirectory);
        bool IsArchive(string path);
    }

    public class ArchiveService : IArchiveService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadsImport);
        private readonly string[] _supportedExtensions = { ".zip", ".rar", ".7z" };

        public bool IsArchive(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLower();
            return _supportedExtensions.Contains(ext);
        }

        public bool Extract(string sourceFile, string destinationDirectory)
        {
            try
            {
                if (!File.Exists(sourceFile)) return false;
                
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                var ext = Path.GetExtension(sourceFile).ToLower();
                _logger.Info($"[ArchiveService] Extracting {sourceFile} to {destinationDirectory}...");

                if (ext == ".zip")
                {
                    ZipFile.ExtractToDirectory(sourceFile, destinationDirectory, overwriteFiles: true);
                    return true;
                }
                else if (ext == ".rar" || ext == ".7z")
                {
                    using (var archive = ArchiveFactory.Open(sourceFile))
                    {
                        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                        {
                            entry.WriteToDirectory(destinationDirectory, new ExtractionOptions()
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[ArchiveService] Extraction failed: {ex.Message}");
            }
            return false;
        }
    }
}
