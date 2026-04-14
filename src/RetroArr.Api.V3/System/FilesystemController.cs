using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RetroArr.Api.V3.IO
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class FilesystemController : ControllerBase
    {
        private static readonly string[] _unixBlockedPrefixes = new[]
        {
            "/proc", "/sys", "/dev", "/run", "/boot",
            "/etc/ssh", "/etc/shadow", "/etc/sudoers",
            "/root", "/var/log", "/var/lib/docker"
        };

        private static readonly string[] _windowsBlockedPrefixes = new[]
        {
            @"C:\Windows\System32\config",
            @"C:\Windows\System32\LogFiles",
            @"C:\Windows\Temp"
        };

        [HttpGet]
        public ActionResult<List<FilesystemItem>> List([FromQuery] string? path = null)
        {
            var currentPath = string.IsNullOrEmpty(path) ? "/" : path;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (currentPath == "/")
                {
                    return Ok(DriveInfo.GetDrives().Select(d => new FilesystemItem
                    {
                        Name = d.Name,
                        Path = d.Name,
                        Type = "drive"
                    }));
                }
            }
            else
            {
                if (currentPath == "") currentPath = "/";
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(currentPath);
            }
            catch (Exception)
            {
                return BadRequest("Invalid path.");
            }

            if (IsBlockedPath(normalized))
            {
                return Forbid();
            }

            if (!Directory.Exists(normalized))
            {
                return NotFound("Path not found");
            }

            try
            {
                var response = new List<FilesystemItem>();

                var parent = Directory.GetParent(normalized);
                if (parent != null)
                {
                    response.Add(new FilesystemItem
                    {
                        Name = "..",
                        Path = parent.FullName,
                        Type = "directory"
                    });
                }

                foreach (var dir in Directory.GetDirectories(normalized))
                {
                    if (IsBlockedPath(dir)) continue;
                    response.Add(new FilesystemItem
                    {
                        Name = Path.GetFileName(dir),
                        Path = dir,
                        Type = "directory"
                    });
                }

                var relevantExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".exe", ".iso", ".bin", ".dmg", ".pkg", ".sh", ".bat", ".cmd"
                };

                foreach (var file in Directory.GetFiles(normalized))
                {
                    var ext = Path.GetExtension(file);
                    if (relevantExtensions.Contains(ext))
                    {
                        response.Add(new FilesystemItem
                        {
                            Name = Path.GetFileName(file),
                            Path = file,
                            Type = "file"
                        });
                    }
                }

                return Ok(response.OrderByDescending(x => x.Type == "directory").ThenBy(x => x.Name));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        private static bool IsBlockedPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return true;

            var prefixes = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? _windowsBlockedPrefixes
                : _unixBlockedPrefixes;
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            foreach (var prefix in prefixes)
            {
                if (fullPath.Equals(prefix, comparison)) return true;
                if (fullPath.StartsWith(prefix + Path.DirectorySeparatorChar, comparison)) return true;
            }
            return false;
        }
    }

    public class FilesystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = "file";
    }
}
