using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.IO
{
    public interface IFileMoverService
    {
        bool ImportFile(string sourceFile, string destinationFile);
    }

    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Globalization", "CA2101:SpecifyMarshalingForPInvokeStringArguments")]
    [SuppressMessage("Microsoft.Interoperability", "CA5392:UseDefaultDllImportSearchPathsAttribute")]
    [SuppressMessage("Microsoft.Globalization", "CA1303:DoNotPassLiteralsAsLocalizedParameters")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class FileMoverService : IFileMoverService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadsImport);
        public bool ImportFile(string sourceFile, string destinationFile)
        {
            if (!File.Exists(sourceFile))
            {
                _logger.Info($"[FileMover] Source file not found: {sourceFile}");
                return false;
            }

            // Ensure destination directory exists
            var destDir = Path.GetDirectoryName(destinationFile);
            if (destDir != null && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // 1. Try Hardlink
            try
            {
                // Note: Hardlinks cannot cross volumes/partitions.
                // If this fails due to cross-volume moves, we catch and fallback to copy.
                if (TryCreateHardLink(sourceFile, destinationFile))
                {
                    _logger.Info($"[FileMover] Hardlink created: {destinationFile} -> {sourceFile}");
                    return true;
                }
                else
                {
                     _logger.Info($"[FileMover] Hardlink creation returned false. Falling back to copy.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[FileMover] Hardlink failed ({ex.Message}). Falling back to copy.");
            }

            // 2. Fallback to Copy
            try
            {
                _logger.Info($"[FileMover] Copying file: {sourceFile} -> {destinationFile}");
                File.Copy(sourceFile, destinationFile, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                 _logger.Error($"[FileMover] Copy failed: {ex.Message}");
                 return false;
            }
        }

        private bool TryCreateHardLink(string source, string destination)
        {
            // Delete destination if it exists (overwrite behavior for import)
            if (File.Exists(destination))
            {
                 File.Delete(destination);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return CreateHardLink(destination, source, IntPtr.Zero);
            }
            else
            {
                // Unix (Linux/macOS)
                return Link(source, destination) == 0;
            }
        }

        // Windows Kernel32
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        // Unix Libc
        [DllImport("libc", SetLastError = true, EntryPoint = "link")]
        private static extern int Link(string oldpath, string newpath);
    }
}
