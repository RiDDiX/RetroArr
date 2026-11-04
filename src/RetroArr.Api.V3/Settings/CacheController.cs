using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Cache;
using RetroArr.Core.Configuration;
using System;
using System.Text.RegularExpressions;
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

        // The Redis password never goes back to the browser. The form gets a placeholder in its
        // place and sends it back unchanged when the password wasn't edited.
        private const string MaskedPlaceholder = "••••••••";
        private static readonly Regex RedisPassword = new(@"(?<=(^|,)\s*password\s*=)[^,]+", RegexOptions.IgnoreCase);
        private const string AddressChangedMessage = "The Redis address changed, re-enter the password.";

        // The placeholder only stands for the stored password while the rest of the connection is
        // unchanged, or any host could collect it. Null when it was sent for another address.
        private string? WithStoredPassword(string connectionString)
        {
            if (!connectionString.Contains(MaskedPlaceholder, StringComparison.Ordinal)) return connectionString;
            var stored = _configService.LoadCacheSettings().ConnectionString ?? string.Empty;
            return RedisPassword.Replace(connectionString, "") == RedisPassword.Replace(stored, "") ? stored : null;
        }

        [HttpGet]
        public ActionResult<CacheSettingsResponse> GetSettings()
        {
            var settings = _configService.LoadCacheSettings();
            return Ok(new CacheSettingsResponse
            {
                Enabled = settings.Enabled,
                ConnectionString = RedisPassword.Replace(settings.ConnectionString ?? string.Empty, MaskedPlaceholder),
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
                var connectionString = WithStoredPassword(request.ConnectionString ?? "localhost:6379");
                if (connectionString == null)
                    return BadRequest(new { error = AddressChangedMessage });

                var settings = new CacheSettings
                {
                    Enabled = request.Enabled,
                    ConnectionString = connectionString,
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
                var connectionString = WithStoredPassword(request.ConnectionString ?? "localhost:6379");
                if (connectionString == null)
                    return Ok(new { success = false, message = AddressChangedMessage });

                // The whole string, so password, ssl and the rest are tested too, not just the endpoint
                var options = StackExchange.Redis.ConfigurationOptions.Parse(connectionString);
                options.ConnectTimeout = 5000;
                options.AbortOnConnectFail = false;
                using var testRedis = StackExchange.Redis.ConnectionMultiplexer.Connect(options);

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
