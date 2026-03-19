using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroArr.Api.V3.IO
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class FilesystemController : ControllerBase
    {
        [HttpGet]
        public ActionResult<List<FilesystemItem>> List([FromQuery] string? path = null)
        {
            var currentPath = string.IsNullOrEmpty(path) ? "/" : path;
            
            // On Windows, handle root listing if path is "/" or empty
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
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
                 // Mac/Linux root
                 if (currentPath == "") currentPath = "/";
            }

            if (!Directory.Exists(currentPath))
            {
                 return NotFound("Path not found");
            }

            try
            {
                var response = new List<FilesystemItem>();
                
                // Add parent directory option
                var parent = Directory.GetParent(currentPath);
                if (parent != null)
                {
                    response.Add(new FilesystemItem 
                    { 
                        Name = "..", 
                        Path = parent.FullName, 
                        Type = "directory" 
                    });
                }

                var dirs = Directory.GetDirectories(currentPath);
                foreach (var dir in dirs)
                {
                    response.Add(new FilesystemItem 
                    { 
                        Name = Path.GetFileName(dir), 
                        Path = dir, 
                        Type = "directory" 
                    });
                }

                // We are primarily looking for Installers (folders) or Executables/ISOs
                var files = Directory.GetFiles(currentPath);
                var relevantExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) 
                { 
                    ".exe", ".iso", ".bin", ".dmg", ".pkg", ".sh", ".bat", ".cmd" 
                };

                foreach (var file in files)
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
            catch (System.UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }

    public class FilesystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = "file"; // drive, directory, file
    }
}
