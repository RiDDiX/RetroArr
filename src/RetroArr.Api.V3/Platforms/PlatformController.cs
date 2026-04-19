using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Platforms
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class PlatformController : ControllerBase
    {
        private static List<Platform> _allPlatforms => PlatformDefinitions.AllPlatforms;

        [HttpGet]
        public ActionResult<List<object>> GetAll([FromQuery] bool? enabledOnly = null)
        {
            var platforms = _allPlatforms.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                slug = p.Slug,
                folderName = p.FolderName,
                type = p.Type.ToString(),
                category = p.Category,
                igdbPlatformId = p.IgdbPlatformId,
                screenScraperSystemId = p.ScreenScraperSystemId,
                parentPlatformId = p.ParentPlatformId,
                enabled = PlatformService.IsEnabled(p.Id, p.Enabled),
                preferredMetadataSource = PlatformService.GetMetadataSource(p.Id)
            }).AsEnumerable();
            
            if (enabledOnly == true)
            {
                platforms = platforms.Where(p => p.enabled);
            }
            
            return Ok(platforms.ToList());
        }

        [HttpGet("{id}")]
        public ActionResult<Platform> GetById(int id)
        {
            var platform = _allPlatforms.FirstOrDefault(p => p.Id == id);
            if (platform == null)
            {
                return NotFound();
            }
            return Ok(platform);
        }

        [HttpGet("by-slug/{slug}")]
        public ActionResult<Platform> GetBySlug(string slug)
        {
            var platform = _allPlatforms.FirstOrDefault(p => 
                p.MatchesFolderName(slug));
            
            if (platform == null)
            {
                return NotFound();
            }
            return Ok(platform);
        }

        [HttpGet("by-igdb/{igdbId}")]
        public ActionResult<Platform> GetByIgdbId(int igdbId)
        {
            var platform = _allPlatforms.FirstOrDefault(p => p.IgdbPlatformId == igdbId);
            if (platform == null)
            {
                return NotFound();
            }
            return Ok(platform);
        }

        [HttpGet("resolve/{igdbPlatformId}")]
        public ActionResult<Platform> ResolvePlatform(int igdbPlatformId)
        {
            var platform = _allPlatforms.FirstOrDefault(p => p.IgdbPlatformId == igdbPlatformId);
            
            if (platform == null)
            {
                return Ok(new Platform { Id = 0, Name = "Unknown", Slug = "unknown", FolderName = "other", Type = PlatformType.Other });
            }

            if (platform.ParentPlatformId.HasValue)
            {
                var parentPlatform = _allPlatforms.FirstOrDefault(p => p.Id == platform.ParentPlatformId);
                if (parentPlatform != null)
                {
                    var result = new Platform
                    {
                        Id = platform.Id,
                        Name = platform.Name,
                        Slug = platform.Slug,
                        FolderName = parentPlatform.FolderName,
                        Type = platform.Type,
                        Category = platform.Category,
                        IgdbPlatformId = platform.IgdbPlatformId,
                        ParentPlatformId = platform.ParentPlatformId,
                        Enabled = platform.Enabled
                    };
                    return Ok(result);
                }
            }

            return Ok(platform);
        }

        [HttpGet("categories")]
        public ActionResult<List<string>> GetCategories()
        {
            var categories = _allPlatforms
                .Where(p => !string.IsNullOrEmpty(p.Category))
                .Select(p => p.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            
            return Ok(categories);
        }

        [HttpGet("by-category/{category}")]
        public ActionResult<List<object>> GetByCategory(string category, [FromQuery] bool? enabledOnly = null)
        {
            var platforms = _allPlatforms
                .Where(p => p.Category != null && p.Category.Equals(category, System.StringComparison.OrdinalIgnoreCase))
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    slug = p.Slug,
                    folderName = p.FolderName,
                    type = p.Type.ToString(),
                    category = p.Category,
                    igdbPlatformId = p.IgdbPlatformId,
                    screenScraperSystemId = p.ScreenScraperSystemId,
                    parentPlatformId = p.ParentPlatformId,
                    enabled = PlatformService.IsEnabled(p.Id, p.Enabled),
                    preferredMetadataSource = PlatformService.GetMetadataSource(p.Id)
                }).AsEnumerable();
            
            if (enabledOnly == true)
            {
                platforms = platforms.Where(p => p.enabled);
            }
            
            return Ok(platforms.ToList());
        }

        [HttpPut("{id}/toggle")]
        public ActionResult TogglePlatform(int id, [FromBody] TogglePlatformRequest request)
        {
            var platform = _allPlatforms.FirstOrDefault(p => p.Id == id);
            if (platform == null)
            {
                return NotFound();
            }

            PlatformService.SetEnabled(id, request.Enabled);

            return Ok(new
            {
                id = platform.Id,
                name = platform.Name,
                slug = platform.Slug,
                folderName = platform.FolderName,
                type = platform.Type.ToString(),
                category = platform.Category,
                igdbPlatformId = platform.IgdbPlatformId,
                parentPlatformId = platform.ParentPlatformId,
                enabled = request.Enabled,
                preferredMetadataSource = PlatformService.GetMetadataSource(id)
            });
        }

        [HttpPut("{id}/metadata-source")]
        public ActionResult SetMetadataSource(int id, [FromBody] SetMetadataSourceRequest request)
        {
            var platform = _allPlatforms.FirstOrDefault(p => p.Id == id);
            if (platform == null)
            {
                return NotFound();
            }

            PlatformService.SetMetadataSource(id, request.Source);

            return Ok(new
            {
                id = platform.Id,
                name = platform.Name,
                preferredMetadataSource = PlatformService.GetMetadataSource(id)
            });
        }

        [HttpPost("auto-enable")]
        public ActionResult AutoEnableFromFolders([FromBody] AutoEnableRequest request)
        {
            if (request.FolderNames == null || !request.FolderNames.Any())
            {
                return BadRequest("No folder names provided");
            }

            foreach (var folderName in request.FolderNames)
            {
                PlatformService.EnablePlatformByFolderName(folderName, _allPlatforms);
            }

            return Ok(new { message = $"Processed {request.FolderNames.Count} folders" });
        }

        public static List<Platform> GetAllPlatformsStatic() => PlatformDefinitions.AllPlatforms;
    }

    public class TogglePlatformRequest
    {
        public bool Enabled { get; set; }
    }

    public class SetMetadataSourceRequest
    {
        public string Source { get; set; } = "igdb";
    }

    public class AutoEnableRequest
    {
        public List<string> FolderNames { get; set; } = new();
    }
}
