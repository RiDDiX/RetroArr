using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using RetroArr.Core.Games;

namespace RetroArr.Core.MetadataSource
{
    /// <summary>
    /// Downloads metadata images and videos into the platform's images/ and videos/
    /// subdirectories following RetroBat/Batocera/EmulationStation naming convention.
    /// </summary>
    public class LocalMediaExportService
    {
        private static readonly Logger _logger = LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly HttpClient _httpClient;

        public LocalMediaExportService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Export all available media for a game to the local platform folder structure.
        /// Files are named: {romBaseName}-{type}.{ext}
        /// </summary>
        public async Task ExportMediaForGameAsync(Game game)
        {
            if (string.IsNullOrEmpty(game.Path))
            {
                _logger.Info($"[LocalMediaExport] Skipping '{game.Title}': no game path set");
                return;
            }

            string? platformDir;
            string? romBaseName;

            if (File.Exists(game.Path))
            {
                platformDir = Path.GetDirectoryName(game.Path);
                romBaseName = Path.GetFileNameWithoutExtension(game.Path);
            }
            else if (Directory.Exists(game.Path))
            {
                var trimmed = game.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                platformDir = Path.GetDirectoryName(trimmed);
                romBaseName = Path.GetFileName(trimmed);
            }
            else
            {
                _logger.Info($"[LocalMediaExport] Skipping '{game.Title}': path not found: {game.Path}");
                return;
            }

            if (string.IsNullOrEmpty(platformDir) || string.IsNullOrEmpty(romBaseName))
                return;

            var imagesDir = Path.Combine(platformDir, "images");
            var videosDir = Path.Combine(platformDir, "videos");

            var images = game.Images;
            if (images == null) return;

            int downloaded = 0;
            int skipped = 0;
            (int d, int s) result;

            // Cover → -thumb
            result = await DownloadIfMissing(images.CoverLargeUrl ?? images.CoverUrl, imagesDir, romBaseName, "thumb");
            downloaded += result.d; skipped += result.s;

            // Screenshot → -image
            if (images.Screenshots != null && images.Screenshots.Count > 0)
            {
                result = await DownloadIfMissing(images.Screenshots[0], imagesDir, romBaseName, "image");
                downloaded += result.d; skipped += result.s;
            }

            // Box back → -boxback
            result = await DownloadIfMissing(images.BoxBackUrl, imagesDir, romBaseName, "boxback");
            downloaded += result.d; skipped += result.s;

            // Wheel/Logo → -marquee
            result = await DownloadIfMissing(images.BannerUrl, imagesDir, romBaseName, "marquee");
            downloaded += result.d; skipped += result.s;

            // Fanart/Background → -fanart (only if different from screenshot)
            var bgUrl = images.BackgroundUrl;
            if (!string.IsNullOrEmpty(bgUrl) && (images.Screenshots == null || images.Screenshots.Count == 0 || bgUrl != images.Screenshots[0]))
            {
                result = await DownloadIfMissing(bgUrl, imagesDir, romBaseName, "fanart");
                downloaded += result.d; skipped += result.s;
            }

            // Video → -video
            result = await DownloadVideoIfMissing(images.VideoUrl, videosDir, romBaseName);
            downloaded += result.d; skipped += result.s;

            if (downloaded > 0 || skipped > 0)
            {
                _logger.Info($"[LocalMediaExport] '{game.Title}': {downloaded} downloaded, {skipped} already existed");
            }
        }

        private async Task<(int d, int s)> DownloadIfMissing(string? url, string targetDir, string baseName, string suffix)
        {
            if (string.IsNullOrEmpty(url))
                return (0, 0);

            if (FileExistsWithAnySuffix(targetDir, baseName, suffix))
                return (0, 1);

            var ext = GuessImageExtension(url);
            var targetPath = Path.Combine(targetDir, $"{baseName}-{suffix}{ext}");
            return await DownloadFile(url, targetPath) ? (1, 0) : (0, 0);
        }

        private async Task<(int d, int s)> DownloadVideoIfMissing(string? url, string targetDir, string baseName)
        {
            if (string.IsNullOrEmpty(url))
                return (0, 0);

            if (FileExistsWithAnySuffix(targetDir, baseName, "video"))
                return (0, 1);

            var ext = url.Contains(".webm", StringComparison.OrdinalIgnoreCase) ? ".webm" : ".mp4";
            var targetPath = Path.Combine(targetDir, $"{baseName}-video{ext}");
            return await DownloadFile(url, targetPath) ? (1, 0) : (0, 0);
        }

        private async Task<bool> DownloadFile(string url, string targetPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Info($"[LocalMediaExport] HTTP {(int)response.StatusCode} for {url}");
                    return false;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);

                _logger.Info($"[LocalMediaExport] Saved: {targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[LocalMediaExport] Failed to download {url}: {ex.Message}");
                return false;
            }
        }

        private static bool FileExistsWithAnySuffix(string dir, string baseName, string suffix)
        {
            if (!Directory.Exists(dir)) return false;

            var pattern = $"{baseName}-{suffix}.*";
            try
            {
                return Directory.GetFiles(dir, pattern).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GuessImageExtension(string url)
        {
            if (url.Contains(".png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (url.Contains(".webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
            if (url.Contains(".gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
            if (url.Contains(".bmp", StringComparison.OrdinalIgnoreCase)) return ".bmp";
            return ".jpg";
        }
    }
}
