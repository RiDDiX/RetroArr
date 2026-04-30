using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;

namespace RetroArr.Api.V3.Discovery
{
    [ApiController]
    [Route("api/v3/discovery")]
    public class DiscoveryController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.ScannerMedia);
        private readonly DiscoveredGameRepository _repo;
        private readonly MediaScannerService _scanner;
        private readonly IGameRepository _gameRepository;
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;

        public DiscoveryController(DiscoveredGameRepository repo, MediaScannerService scanner, IGameRepository gameRepository, IGameMetadataServiceFactory metadataServiceFactory)
        {
            _repo = repo;
            _scanner = scanner;
            _gameRepository = gameRepository;
            _metadataServiceFactory = metadataServiceFactory;
        }

        [HttpGet]
        public async Task<ActionResult> List()
        {
            var items = await _repo.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("count")]
        public async Task<ActionResult> Count()
        {
            return Ok(new { count = await _repo.CountAsync() });
        }

        public class ScanRequest
        {
            public string? FolderPath { get; set; }
            public string? Platform { get; set; }
        }

        [HttpPost("scan")]
        public IActionResult TriggerDiscoveryScan([FromBody] ScanRequest? request = null)
        {
            if (_scanner.IsScanning)
                return Conflict(new { success = false, message = "Another scan is already running." });

            _logger.Info($"[Discovery] TriggerScan FolderPath='{request?.FolderPath}' Platform='{request?.Platform}'");
            _ = Task.Run(async () =>
            {
                try
                {
                    var added = await _scanner.DiscoverOnlyAsync(request?.FolderPath, request?.Platform);
                    _logger.Info($"[Discovery] Background scan finished, {added} discovery item(s) persisted");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Discovery] Background scan error: {ex}");
                }
            });

            return Ok(new { success = true, message = "Discovery scan started in background." });
        }

        public class ImportRequest
        {
            public List<int> Ids { get; set; } = new List<int>();
            public string? MetadataSource { get; set; }
        }

        [HttpPost("import")]
        public async Task<ActionResult> ImportSelected([FromBody] ImportRequest request)
        {
            if (request == null || request.Ids == null || request.Ids.Count == 0)
                return BadRequest(new { success = false, message = "No discovery ids supplied." });

            var imported = 0;
            var failed = new List<int>();
            foreach (var id in request.Ids.Distinct())
            {
                var item = await _repo.GetByIdAsync(id);
                if (item == null) { failed.Add(id); continue; }

                try
                {
                    var ok = await _scanner.ImportDiscoveredAsync(item, request.MetadataSource);
                    if (ok)
                    {
                        await _repo.DeleteAsync(id);
                        imported++;
                    }
                    else
                    {
                        failed.Add(id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Discovery] Import {id} failed: {ex.Message}");
                    failed.Add(id);
                }
            }

            var remaining = await _repo.CountAsync();
            return Ok(new { success = true, imported, failed = failed.Count, remaining });
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var ok = await _repo.DeleteAsync(id);
            if (!ok) return NotFound();
            return Ok(new { success = true, remaining = await _repo.CountAsync() });
        }

        public class BulkDeleteRequest
        {
            public List<int> Ids { get; set; } = new List<int>();
        }

        [HttpPost("bulk-delete")]
        public async Task<ActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
        {
            if (request == null || request.Ids == null || request.Ids.Count == 0)
                return BadRequest(new { success = false, message = "No ids supplied." });

            var removed = await _repo.DeleteManyAsync(request.Ids);
            return Ok(new { success = true, removed, remaining = await _repo.CountAsync() });
        }

        [HttpDelete]
        public async Task<ActionResult> ClearAll()
        {
            var removed = await _repo.ClearAllAsync();
            return Ok(new { success = true, removed });
        }
    }
}
