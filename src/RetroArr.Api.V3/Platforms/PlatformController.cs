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

        /* Platform definitions moved to RetroArr.Core.Games.PlatformDefinitions
           Old inline definition removed to avoid duplication
        private static readonly List<Platform> _allPlatformsOld = new()
        {
            // ========== PC / Computer ==========
            new Platform { Id = 1, Name = "PC (Windows)", Slug = "pc", FolderName = "windows", Type = PlatformType.PC, Category = "Computer", IgdbPlatformId = 6, Enabled = true },
            new Platform { Id = 2, Name = "macOS", Slug = "macos", FolderName = "macintosh", Type = PlatformType.MacOS, Category = "Computer", IgdbPlatformId = 14, Enabled = true },
            new Platform { Id = 3, Name = "MS-DOS", Slug = "dos", FolderName = "dos", Type = PlatformType.DOS, Category = "Computer", IgdbPlatformId = 13, Enabled = true },
            new Platform { Id = 4, Name = "Amiga", Slug = "amiga", FolderName = "amiga", Type = PlatformType.Amiga, Category = "Computer", IgdbPlatformId = 16, Enabled = false },
            new Platform { Id = 5, Name = "Amiga CD32", Slug = "cd32", FolderName = "cd32", Type = PlatformType.AmigaCD32, Category = "Computer", IgdbPlatformId = 114, Enabled = false },
            new Platform { Id = 6, Name = "Commodore 64", Slug = "c64", FolderName = "c64", Type = PlatformType.Commodore64, Category = "Computer", IgdbPlatformId = 15, Enabled = false },
            new Platform { Id = 7, Name = "Commodore VIC-20", Slug = "vic20", FolderName = "vic20", Type = PlatformType.CommodoreVIC20, Category = "Computer", IgdbPlatformId = 71, Enabled = false },
            new Platform { Id = 8, Name = "ZX Spectrum", Slug = "zxspectrum", FolderName = "zxspectrum", Type = PlatformType.ZXSpectrum, Category = "Computer", IgdbPlatformId = 26, Enabled = false },
            new Platform { Id = 9, Name = "MSX", Slug = "msx", FolderName = "msx", Type = PlatformType.MSX, Category = "Computer", IgdbPlatformId = 27, Enabled = false },
            new Platform { Id = 10, Name = "MSX2", Slug = "msx2", FolderName = "msx2", Type = PlatformType.MSX2, Category = "Computer", IgdbPlatformId = 53, Enabled = false },
            new Platform { Id = 11, Name = "Sharp X68000", Slug = "x68000", FolderName = "x68000", Type = PlatformType.SharpX68000, Category = "Computer", IgdbPlatformId = 121, Enabled = false },
            new Platform { Id = 12, Name = "Apple II", Slug = "apple2", FolderName = "apple2", Type = PlatformType.AppleII, Category = "Computer", IgdbPlatformId = 75, Enabled = false },
            new Platform { Id = 13, Name = "BBC Micro", Slug = "bbcmicro", FolderName = "bbcmicro", Type = PlatformType.BBCMicro, Category = "Computer", IgdbPlatformId = 69, Enabled = false },
            
            // ========== Sony PlayStation ==========
            new Platform { Id = 20, Name = "PlayStation 1", Slug = "ps1", FolderName = "psx", Type = PlatformType.PlayStation, Category = "Sony", IgdbPlatformId = 7, Enabled = true },
            new Platform { Id = 21, Name = "PlayStation 2", Slug = "ps2", FolderName = "ps2", Type = PlatformType.PlayStation2, Category = "Sony", IgdbPlatformId = 8, Enabled = true },
            new Platform { Id = 22, Name = "PlayStation 3", Slug = "ps3", FolderName = "ps3", Type = PlatformType.PlayStation3, Category = "Sony", IgdbPlatformId = 9, Enabled = true },
            new Platform { Id = 23, Name = "PlayStation 4", Slug = "ps4", FolderName = "ps4", Type = PlatformType.PlayStation4, Category = "Sony", IgdbPlatformId = 48, Enabled = true },
            new Platform { Id = 24, Name = "PlayStation 5", Slug = "ps5", FolderName = "ps5", Type = PlatformType.PlayStation5, Category = "Sony", IgdbPlatformId = 167, Enabled = true },
            new Platform { Id = 25, Name = "PlayStation Portable", Slug = "psp", FolderName = "psp", Type = PlatformType.PSP, Category = "Sony", IgdbPlatformId = 38, Enabled = true },
            new Platform { Id = 26, Name = "PlayStation Vita", Slug = "vita", FolderName = "vita", Type = PlatformType.PSVita, Category = "Sony", IgdbPlatformId = 46, Enabled = true },
            
            // ========== Microsoft Xbox ==========
            new Platform { Id = 30, Name = "Xbox", Slug = "xbox", FolderName = "xbox", Type = PlatformType.Xbox, Category = "Microsoft", IgdbPlatformId = 11, Enabled = false },
            new Platform { Id = 31, Name = "Xbox 360", Slug = "xbox360", FolderName = "xbox360", Type = PlatformType.Xbox360, Category = "Microsoft", IgdbPlatformId = 12, Enabled = false },
            new Platform { Id = 32, Name = "Xbox One", Slug = "xboxone", FolderName = "xboxone", Type = PlatformType.XboxOne, Category = "Microsoft", IgdbPlatformId = 49, Enabled = false },
            new Platform { Id = 33, Name = "Xbox Series X|S", Slug = "xboxseriesx", FolderName = "xboxseriesx", Type = PlatformType.XboxSeriesX, Category = "Microsoft", IgdbPlatformId = 169, Enabled = false },
            
            // ========== Nintendo Home Consoles ==========
            new Platform { Id = 40, Name = "Nintendo Entertainment System", Slug = "nes", FolderName = "nes", Type = PlatformType.NES, Category = "Nintendo", IgdbPlatformId = 18, Enabled = true },
            new Platform { Id = 41, Name = "Super Nintendo (SNES)", Slug = "snes", FolderName = "snes", Type = PlatformType.SNES, Category = "Nintendo", IgdbPlatformId = 19, Enabled = true },
            new Platform { Id = 42, Name = "Nintendo 64", Slug = "n64", FolderName = "n64", Type = PlatformType.Nintendo64, Category = "Nintendo", IgdbPlatformId = 4, Enabled = true },
            new Platform { Id = 43, Name = "Nintendo GameCube", Slug = "gamecube", FolderName = "gamecube", Type = PlatformType.GameCube, Category = "Nintendo", IgdbPlatformId = 21, Enabled = true },
            new Platform { Id = 44, Name = "Nintendo Wii", Slug = "wii", FolderName = "wii", Type = PlatformType.Wii, Category = "Nintendo", IgdbPlatformId = 5, Enabled = true },
            new Platform { Id = 45, Name = "Nintendo Wii U", Slug = "wiiu", FolderName = "wiiu", Type = PlatformType.WiiU, Category = "Nintendo", IgdbPlatformId = 41, Enabled = true },
            new Platform { Id = 46, Name = "Nintendo Switch", Slug = "switch", FolderName = "switch", Type = PlatformType.Switch, Category = "Nintendo", IgdbPlatformId = 130, Enabled = true },
            new Platform { Id = 47, Name = "Nintendo Switch 2", Slug = "switch2", FolderName = "switch", Type = PlatformType.Switch2, Category = "Nintendo", IgdbPlatformId = 441, ParentPlatformId = 46, Enabled = true },
            new Platform { Id = 48, Name = "Famicom Disk System", Slug = "fds", FolderName = "fds", Type = PlatformType.FamicomDiskSystem, Category = "Nintendo", IgdbPlatformId = 51, Enabled = false },
            new Platform { Id = 49, Name = "Super Famicom", Slug = "sfc", FolderName = "sfc", Type = PlatformType.SuperFamicom, Category = "Nintendo", IgdbPlatformId = 58, ParentPlatformId = 41, Enabled = false },
            
            // ========== Nintendo Handhelds ==========
            new Platform { Id = 50, Name = "Game Boy", Slug = "gb", FolderName = "gb", Type = PlatformType.GameBoy, Category = "Nintendo", IgdbPlatformId = 33, Enabled = true },
            new Platform { Id = 51, Name = "Game Boy Color", Slug = "gbc", FolderName = "gbc", Type = PlatformType.GameBoyColor, Category = "Nintendo", IgdbPlatformId = 22, Enabled = true },
            new Platform { Id = 52, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba", Type = PlatformType.GameBoyAdvance, Category = "Nintendo", IgdbPlatformId = 24, Enabled = true },
            new Platform { Id = 53, Name = "Nintendo DS", Slug = "nds", FolderName = "nds", Type = PlatformType.NintendoDS, Category = "Nintendo", IgdbPlatformId = 20, Enabled = true },
            new Platform { Id = 54, Name = "Nintendo 3DS", Slug = "3ds", FolderName = "3ds", Type = PlatformType.Nintendo3DS, Category = "Nintendo", IgdbPlatformId = 37, Enabled = true },
            new Platform { Id = 55, Name = "Virtual Boy", Slug = "virtualboy", FolderName = "virtualboy", Type = PlatformType.VirtualBoy, Category = "Nintendo", IgdbPlatformId = 87, Enabled = false },
            new Platform { Id = 56, Name = "Pokémon Mini", Slug = "pokemini", FolderName = "pokemini", Type = PlatformType.PokemonMini, Category = "Nintendo", IgdbPlatformId = 152, Enabled = false },
            
            // ========== Sega ==========
            new Platform { Id = 60, Name = "SG-1000", Slug = "sg1000", FolderName = "sg1000", Type = PlatformType.SG1000, Category = "Sega", IgdbPlatformId = 84, Enabled = false },
            new Platform { Id = 61, Name = "Master System", Slug = "mastersystem", FolderName = "mastersystem", Type = PlatformType.MasterSystem, Category = "Sega", IgdbPlatformId = 64, Enabled = true },
            new Platform { Id = 62, Name = "Mega Drive / Genesis", Slug = "megadrive", FolderName = "megadrive", Type = PlatformType.MegaDrive, Category = "Sega", IgdbPlatformId = 29, Enabled = true },
            new Platform { Id = 63, Name = "Mega CD / Sega CD", Slug = "segacd", FolderName = "segacd", Type = PlatformType.SegaCD, Category = "Sega", IgdbPlatformId = 78, Enabled = true },
            new Platform { Id = 64, Name = "Sega 32X", Slug = "32x", FolderName = "32x", Type = PlatformType.Sega32X, Category = "Sega", IgdbPlatformId = 30, Enabled = false },
            new Platform { Id = 65, Name = "Game Gear", Slug = "gamegear", FolderName = "gamegear", Type = PlatformType.GameGear, Category = "Sega", IgdbPlatformId = 35, Enabled = true },
            new Platform { Id = 66, Name = "Sega Saturn", Slug = "saturn", FolderName = "saturn", Type = PlatformType.Saturn, Category = "Sega", IgdbPlatformId = 32, Enabled = true },
            new Platform { Id = 67, Name = "Dreamcast", Slug = "dreamcast", FolderName = "dreamcast", Type = PlatformType.Dreamcast, Category = "Sega", IgdbPlatformId = 23, Enabled = true },
            new Platform { Id = 68, Name = "Naomi", Slug = "naomi", FolderName = "naomi", Type = PlatformType.Naomi, Category = "Sega", IgdbPlatformId = 52, Enabled = false },
            new Platform { Id = 69, Name = "Naomi 2", Slug = "naomi2", FolderName = "naomi2", Type = PlatformType.Naomi2, Category = "Sega", IgdbPlatformId = 122, Enabled = false },
            new Platform { Id = 70, Name = "Atomiswave", Slug = "atomiswave", FolderName = "atomiswave", Type = PlatformType.Atomiswave, Category = "Sega", IgdbPlatformId = 123, Enabled = false },
            
            // ========== Atari ==========
            new Platform { Id = 80, Name = "Atari 2600", Slug = "atari2600", FolderName = "atari2600", Type = PlatformType.Atari2600, Category = "Atari", IgdbPlatformId = 59, Enabled = false },
            new Platform { Id = 81, Name = "Atari 5200", Slug = "atari5200", FolderName = "atari5200", Type = PlatformType.Atari5200, Category = "Atari", IgdbPlatformId = 66, Enabled = false },
            new Platform { Id = 82, Name = "Atari 7800", Slug = "atari7800", FolderName = "atari7800", Type = PlatformType.Atari7800, Category = "Atari", IgdbPlatformId = 60, Enabled = false },
            new Platform { Id = 83, Name = "Atari Jaguar", Slug = "jaguar", FolderName = "jaguar", Type = PlatformType.Jaguar, Category = "Atari", IgdbPlatformId = 62, Enabled = false },
            new Platform { Id = 84, Name = "Atari Jaguar CD", Slug = "jaguarcd", FolderName = "jaguarcd", Type = PlatformType.JaguarCD, Category = "Atari", IgdbPlatformId = 171, Enabled = false },
            new Platform { Id = 85, Name = "Atari Lynx", Slug = "lynx", FolderName = "lynx", Type = PlatformType.Lynx, Category = "Atari", IgdbPlatformId = 61, Enabled = false },
            new Platform { Id = 86, Name = "Atari ST", Slug = "atarist", FolderName = "atarist", Type = PlatformType.AtariST, Category = "Atari", IgdbPlatformId = 63, Enabled = false },
            
            // ========== NEC / PC Engine ==========
            new Platform { Id = 90, Name = "PC Engine / TurboGrafx-16", Slug = "pcengine", FolderName = "pcengine", Type = PlatformType.PCEngine, Category = "NEC", IgdbPlatformId = 86, Enabled = true },
            new Platform { Id = 91, Name = "PC Engine CD", Slug = "pcenginecd", FolderName = "pcenginecd", Type = PlatformType.PCEngineCD, Category = "NEC", IgdbPlatformId = 150, Enabled = false },
            new Platform { Id = 92, Name = "SuperGrafx", Slug = "supergrafx", FolderName = "supergrafx", Type = PlatformType.SuperGrafx, Category = "NEC", IgdbPlatformId = 128, Enabled = false },
            
            // ========== Arcade ==========
            new Platform { Id = 100, Name = "Arcade (MAME)", Slug = "arcade", FolderName = "arcade", Type = PlatformType.Arcade, Category = "Arcade", IgdbPlatformId = 52, Enabled = true },
            new Platform { Id = 101, Name = "FinalBurn Neo", Slug = "fbneo", FolderName = "fbneo", Type = PlatformType.FinalBurnNeo, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 102, Name = "Neo Geo", Slug = "neogeo", FolderName = "neogeo", Type = PlatformType.NeoGeo, Category = "Arcade", IgdbPlatformId = 79, Enabled = true },
            new Platform { Id = 103, Name = "Neo Geo CD", Slug = "neogeocd", FolderName = "neogeocd", Type = PlatformType.NeoGeoCD, Category = "Arcade", IgdbPlatformId = 136, Enabled = false },
            new Platform { Id = 104, Name = "CPS-1", Slug = "cps1", FolderName = "cps1", Type = PlatformType.CPS1, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 105, Name = "CPS-2", Slug = "cps2", FolderName = "cps2", Type = PlatformType.CPS2, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 106, Name = "CPS-3", Slug = "cps3", FolderName = "cps3", Type = PlatformType.CPS3, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 107, Name = "Daphne (Laserdisc)", Slug = "daphne", FolderName = "daphne", Type = PlatformType.Daphne, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            
            // ========== Handhelds & Others ==========
            new Platform { Id = 110, Name = "WonderSwan", Slug = "wonderswan", FolderName = "wonderswan", Type = PlatformType.WonderSwan, Category = "Handhelds", IgdbPlatformId = 57, Enabled = false },
            new Platform { Id = 111, Name = "WonderSwan Color", Slug = "wonderswancolor", FolderName = "wonderswancolor", Type = PlatformType.WonderSwanColor, Category = "Handhelds", IgdbPlatformId = 57, Enabled = false },
            new Platform { Id = 112, Name = "Neo Geo Pocket", Slug = "ngp", FolderName = "ngp", Type = PlatformType.NeoGeoPocket, Category = "Handhelds", IgdbPlatformId = 119, Enabled = false },
            new Platform { Id = 113, Name = "Neo Geo Pocket Color", Slug = "ngpc", FolderName = "ngpc", Type = PlatformType.NeoGeoPocketColor, Category = "Handhelds", IgdbPlatformId = 120, Enabled = false },
            new Platform { Id = 114, Name = "Watara Supervision", Slug = "supervision", FolderName = "supervision", Type = PlatformType.WataraSupervision, Category = "Handhelds", IgdbPlatformId = 95, Enabled = false },
            
            // ========== Special / Modern ==========
            new Platform { Id = 120, Name = "ScummVM", Slug = "scummvm", FolderName = "scummvm", Type = PlatformType.ScummVM, Category = "Special", IgdbPlatformId = null, Enabled = true },
            new Platform { Id = 121, Name = "DOSBox", Slug = "dosbox", FolderName = "dosbox", Type = PlatformType.DOSBox, Category = "Special", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 122, Name = "OpenBOR", Slug = "openbor", FolderName = "openbor", Type = PlatformType.OpenBOR, Category = "Special", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 123, Name = "Ports", Slug = "ports", FolderName = "ports", Type = PlatformType.Ports, Category = "Special", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 124, Name = "Moonlight", Slug = "moonlight", FolderName = "moonlight", Type = PlatformType.Moonlight, Category = "Special", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 125, Name = "Steam", Slug = "steam", FolderName = "steam", Type = PlatformType.Steam, Category = "Special", IgdbPlatformId = 6, Enabled = true },
        };
        */

        [HttpGet]
        public ActionResult<List<Platform>> GetAll([FromQuery] bool? enabledOnly = null)
        {
            var platforms = _allPlatforms.Select(p => new Platform
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                FolderName = p.FolderName,
                Type = p.Type,
                Category = p.Category,
                IgdbPlatformId = p.IgdbPlatformId,
                ParentPlatformId = p.ParentPlatformId,
                Enabled = PlatformService.IsEnabled(p.Id, p.Enabled)
            }).AsEnumerable();
            
            if (enabledOnly == true)
            {
                platforms = platforms.Where(p => p.Enabled);
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
        public ActionResult<List<Platform>> GetByCategory(string category, [FromQuery] bool? enabledOnly = null)
        {
            var platforms = _allPlatforms
                .Where(p => p.Category != null && p.Category.Equals(category, System.StringComparison.OrdinalIgnoreCase))
                .Select(p => new Platform
                {
                    Id = p.Id,
                    Name = p.Name,
                    Slug = p.Slug,
                    FolderName = p.FolderName,
                    Type = p.Type,
                    Category = p.Category,
                    IgdbPlatformId = p.IgdbPlatformId,
                    ParentPlatformId = p.ParentPlatformId,
                    Enabled = PlatformService.IsEnabled(p.Id, p.Enabled)
                });
            
            if (enabledOnly == true)
            {
                platforms = platforms.Where(p => p.Enabled);
            }
            
            return Ok(platforms.ToList());
        }

        [HttpPut("{id}/toggle")]
        public ActionResult<Platform> TogglePlatform(int id, [FromBody] TogglePlatformRequest request)
        {
            var platform = _allPlatforms.FirstOrDefault(p => p.Id == id);
            if (platform == null)
            {
                return NotFound();
            }

            PlatformService.SetEnabled(id, request.Enabled);

            return Ok(new Platform
            {
                Id = platform.Id,
                Name = platform.Name,
                Slug = platform.Slug,
                FolderName = platform.FolderName,
                Type = platform.Type,
                Category = platform.Category,
                IgdbPlatformId = platform.IgdbPlatformId,
                ParentPlatformId = platform.ParentPlatformId,
                Enabled = request.Enabled
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

    public class AutoEnableRequest
    {
        public List<string> FolderNames { get; set; } = new();
    }
}
