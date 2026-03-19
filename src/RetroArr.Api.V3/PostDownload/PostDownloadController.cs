using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Configuration;

namespace RetroArr.Api.V3.PostDownload
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class PostDownloadController : ControllerBase
    {
        private readonly ConfigurationService _configService;

        public PostDownloadController(ConfigurationService configService)
        {
            _configService = configService;
        }

        [HttpGet]
        public ActionResult<PostDownloadSettings> GetSettings()
        {
            try
            {
                return Ok(_configService.LoadPostDownloadSettings());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error loading post-download settings: {ex.Message}" });
            }
        }

        [HttpPost]
        public ActionResult SaveSettings([FromBody] PostDownloadSettings settings)
        {
            try
            {
                _configService.SavePostDownloadSettings(settings);
                return Ok(new { message = "Post-download settings saved successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error saving post-download settings: {ex.Message}" });
            }
        }
    }
}
