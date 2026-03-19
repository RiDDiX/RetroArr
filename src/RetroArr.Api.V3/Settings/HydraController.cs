using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Configuration;
using RetroArr.Core.Indexers;

namespace RetroArr.Api.V3.Settings
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class HydraController : ControllerBase
    {
        private readonly ConfigurationService _configurationService;

        public HydraController(ConfigurationService configurationService)
        {
            _configurationService = configurationService;
        }

        [HttpGet]
        public ActionResult<List<HydraConfiguration>> GetSources()
        {
            return Ok(_configurationService.LoadHydraIndexers());
        }

        [HttpPost]
        public ActionResult<HydraConfiguration> AddSource([FromBody] HydraConfiguration source)
        {
            var sources = _configurationService.LoadHydraIndexers();
            
            // Assign ID
            source.Id = (sources.Count > 0 ? sources.Max(i => i.Id) : 0) + 1;
            
            sources.Add(source);
            _configurationService.SaveHydraIndexers(sources);
            
            return Ok(source);
        }

        [HttpPut("{id}")]
        public ActionResult<HydraConfiguration> UpdateSource(int id, [FromBody] HydraConfiguration source)
        {
            var sources = _configurationService.LoadHydraIndexers();
            var existing = sources.FirstOrDefault(s => s.Id == id);
            
            if (existing == null) return NotFound();

            existing.Name = source.Name;
            existing.Url = source.Url;
            existing.Enabled = source.Enabled;

            _configurationService.SaveHydraIndexers(sources);
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public ActionResult DeleteSource(int id)
        {
            var sources = _configurationService.LoadHydraIndexers();
            var existing = sources.FirstOrDefault(s => s.Id == id);
            
            if (existing == null) return NotFound();

            sources.Remove(existing);
            _configurationService.SaveHydraIndexers(sources);
            return Ok();
        }
    }
}
