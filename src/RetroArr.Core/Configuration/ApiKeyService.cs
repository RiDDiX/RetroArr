using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace RetroArr.Core.Configuration
{
    public class ApiKeyService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Configuration);
        private readonly string _keyFilePath;
        private readonly object _lock = new();
        private string _cachedKey = string.Empty;

        public ApiKeyService(ConfigurationService configService)
        {
            _keyFilePath = Path.Combine(configService.GetConfigDirectory(), "apikey.json");
            Load();
        }

        public string GetApiKey()
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_cachedKey))
                {
                    _cachedKey = Generate();
                    Persist(_cachedKey);
                }
                return _cachedKey;
            }
        }

        public string Regenerate()
        {
            lock (_lock)
            {
                _cachedKey = Generate();
                Persist(_cachedKey);
                return _cachedKey;
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_keyFilePath))
                {
                    var json = File.ReadAllText(_keyFilePath);
                    var record = JsonSerializer.Deserialize<ApiKeyRecord>(json);
                    if (record != null && !string.IsNullOrWhiteSpace(record.Key))
                    {
                        _cachedKey = record.Key;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[ApiKey] failed to read {_keyFilePath}: {ex.Message}. Generating a new key.");
            }
            _cachedKey = Generate();
            Persist(_cachedKey);
        }

        private void Persist(string key)
        {
            try
            {
                var record = new ApiKeyRecord { Key = key, CreatedAt = DateTime.UtcNow };
                File.WriteAllText(_keyFilePath, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _logger.Error($"[ApiKey] failed to persist {_keyFilePath}: {ex.Message}. Key will only live in memory until the next restart.");
            }
        }

        private static string Generate()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private sealed class ApiKeyRecord
        {
            public string Key { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }
    }
}
