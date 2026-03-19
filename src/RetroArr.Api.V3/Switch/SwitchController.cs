using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Switch;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RetroArr.Api.V3.Switch
{
    [ApiController]
    [Route("api/v3/nsw")]
    public class SwitchController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.Switch);
        static SwitchController() {
            _logger.Info("[DEBUG] SwitchController type initialized");
        }
        private readonly ISwitchUsbService _usbService;

        public SwitchController(ISwitchUsbService usbService)
        {
            _usbService = usbService;
        }

        [HttpGet("devices")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public ActionResult<List<string>> GetDevices()
        {
            _logger.Info("[API] GET /devices requested");
            var devices = _usbService.ScanDevices();
            return Ok(devices);
        }

        [HttpPost("install")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult InstallGame([FromBody] InstallRequest request)
        {
            _logger.Info($"[API] POST /install requested for {request.FilePath}");
            if (string.IsNullOrEmpty(request.FilePath))
                return BadRequest("File path is required");

            var targetDevice = request.DeviceId;
            
            // Start installation in a background task to prevent HTTP timeouts
            _ = Task.Run(async () => {
                try {
                    _logger.Info($"[API] Background task started for {request.FilePath}");
                    var progress = new System.Progress<double>();
                    await _usbService.InstallGameAsync(request.FilePath, targetDevice, progress, CancellationToken.None);
                    _logger.Info($"[API] Background task finished for {request.FilePath}");
                } catch (System.Exception ex) {
                    _logger.Info($"[API] Background task ERROR for {request.FilePath}: {ex.Message}");
                }
            });
            
            return Ok(new { status = "Installation started in background" });
        }

        [HttpGet("progress")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult GetStatus()
        {
            _logger.Info($"[API] GET /progress requested. Current Status: {_usbService.CurrentStatus}");
            return Ok(new { 
                progress = _usbService.CurrentProgress,
                status = _usbService.CurrentStatus
            });
        }

        [HttpPost("cancel")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult CancelInstallation()
        {
            _logger.Info("[API] POST /cancel requested");
            _usbService.CancelCurrentInstallation();
            return Ok(new { status = "Cancellation requested" });
        }

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("Pong from SwitchController");
        }
    }

    public class InstallRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
    }
}
