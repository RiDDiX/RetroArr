using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;
using StackExchange.Redis;

namespace RetroArr.Core.Cache
{
    public class RedisCacheService : ICacheService, IDisposable
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly CacheSettings _settings;
        private bool _disposed;

        public bool IsEnabled => true;

        public RedisCacheService(CacheSettings settings)
        {
            _settings = settings;
            _redis = ConnectionMultiplexer.Connect(settings.ConnectionString);
            _db = _redis.GetDatabase();
            _logger.Info($"[Cache] Redis connected: {settings.ConnectionString}");
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            try
            {
                var value = await _db.StringGetAsync(key);
                if (value.IsNullOrEmpty) return null;
                return JsonSerializer.Deserialize<T>((string)value!);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Cache] GET error for '{key}': {ex.Message}");
                return null;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
        {
            try
            {
                var json = JsonSerializer.Serialize(value);
                await _db.StringSetAsync(key, json, ttl);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Cache] SET error for '{key}': {ex.Message}");
            }
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) where T : class
        {
            var cached = await GetAsync<T>(key);
            if (cached != null) return cached;

            var value = await factory();
            await SetAsync(key, value, ttl);
            return value;
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                await _db.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Cache] DELETE error for '{key}': {ex.Message}");
            }
        }

        public async Task RemoveByPrefixAsync(string prefix)
        {
            try
            {
                var server = _redis.GetServers().FirstOrDefault();
                if (server == null) return;

                var keys = server.Keys(pattern: $"{prefix}*").ToArray();
                if (keys.Length > 0)
                {
                    await _db.KeyDeleteAsync(keys);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Cache] DELETE prefix error for '{prefix}': {ex.Message}");
            }
        }

        public async Task FlushAsync()
        {
            try
            {
                await RemoveByPrefixAsync("retroarr:");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Cache] FLUSH error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _redis?.Dispose();
                _disposed = true;
            }
        }
    }
}
