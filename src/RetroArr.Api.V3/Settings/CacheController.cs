using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Cache;
using RetroArr.Core.Configuration;
using System;
using System.Threading.Tasks;

namespace RetroArr.Api.V3.Settings
{
    [ApiController]
    [Route("api/v3/settings/cache")]
    public class CacheController : ControllerBase
    {
        private readonly ConfigurationService _configService;
        private readonly ICacheService _cacheService;

        public CacheController(ConfigurationService configService, ICacheService cacheService)
        {
            _configService = configService;
            _cacheService = cacheService;
        }

        [HttpGet]
        public ActionResult<CacheSettingsResponse> GetSettings()
        {
            var settings = _configService.LoadCacheSettings();
            return Ok(new CacheSettingsResponse
            {
                Enabled = settings.Enabled,
                ConnectionString = settings.ConnectionString,
                LibraryListTtlSeconds = settings.LibraryListTtlSeconds,
                GameDetailTtlSeconds = settings.GameDetailTtlSeconds,
                MetadataTtlSeconds = settings.MetadataTtlSeconds,
                DownloadStatusTtlSeconds = settings.DownloadStatusTtlSeconds,
                DbStatsTtlSeconds = settings.DbStatsTtlSeconds,
                IsConnected = _cacheService.IsEnabled
            });
        }

        [HttpPut]
        public ActionResult SaveSettings([FromBody] CacheSettingsRequest request)
        {
            try
            {
                var settings = new CacheSettings
                {
                    Enabled = request.Enabled,
                    ConnectionString = request.ConnectionString ?? "localhost:6379",
                    LibraryListTtlSeconds = request.LibraryListTtlSeconds ?? 60,
                    GameDetailTtlSeconds = request.GameDetailTtlSeconds ?? 120,
                    MetadataTtlSeconds = request.MetadataTtlSeconds ?? 3600,
                    DownloadStatusTtlSeconds = request.DownloadStatusTtlSeconds ?? 30,
                    DbStatsTtlSeconds = request.DbStatsTtlSeconds ?? 300
                };

                _configService.SaveCacheSettings(settings);
                return Ok(new
                {
                    message = "Cache settings saved. Restart required for connection changes.",
                    restartRequired = true
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("test")]
        public ActionResult TestConnection([FromBody] CacheTestRequest request)
        {
            try
            {
                var connectionString = request.ConnectionString ?? "localhost:6379";
                using var testRedis = StackExchange.Redis.ConnectionMultiplexer.Connect(
                    new StackExchange.Redis.ConfigurationOptions
                    {
                        EndPoints = { connectionString },
                        ConnectTimeout = 5000,
                        AbortOnConnectFail = false
                    });

                if (testRedis.IsConnected)
                {
                    var db = testRedis.GetDatabase();
                    db.Ping();
                    return Ok(new { success = true, message = "Redis connection successful!" });
                }
                else
                {
                    return Ok(new { success = false, message = "Could not connect to Redis server." });
                }
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = $"Connection failed: {ex.Message}" });
            }
        }

        [HttpPost("clear")]
        public async Task<ActionResult> ClearCache()
        {
            try
            {
                await _cacheService.FlushAsync();
                return Ok(new { success = true, message = "Cache cleared." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
        }
    }

    public class CacheSettingsRequest
    {
        public bool Enabled { get; set; }
        public string? ConnectionString { get; set; }
        public int? LibraryListTtlSeconds { get; set; }
        public int? GameDetailTtlSeconds { get; set; }
        public int? MetadataTtlSeconds { get; set; }
        public int? DownloadStatusTtlSeconds { get; set; }
        public int? DbStatsTtlSeconds { get; set; }
    }

    public class CacheSettingsResponse
    {
        public bool Enabled { get; set; }
        public string ConnectionString { get; set; } = "localhost:6379";
        public int LibraryListTtlSeconds { get; set; } = 60;
        public int GameDetailTtlSeconds { get; set; } = 120;
        public int MetadataTtlSeconds { get; set; } = 3600;
        public int DownloadStatusTtlSeconds { get; set; } = 30;
        public int DbStatsTtlSeconds { get; set; } = 300;
        public bool IsConnected { get; set; }
    }

    public class CacheTestRequest
    {
        public string? ConnectionString { get; set; }
    }
}
