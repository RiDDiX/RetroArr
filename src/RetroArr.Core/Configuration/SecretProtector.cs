using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;

namespace RetroArr.Core.Configuration
{
    public class SecretProtector
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Configuration);
        public const string Prefix = "__enc__:";
        private readonly IDataProtector _protector;

        public SecretProtector(string keyDirectory)
        {
            if (string.IsNullOrWhiteSpace(keyDirectory))
                throw new ArgumentException("Key directory is required.", nameof(keyDirectory));

            Directory.CreateDirectory(keyDirectory);

            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(keyDirectory),
                builder => builder.SetApplicationName("RetroArr"));

            _protector = provider.CreateProtector("RetroArr.Configuration.Secrets.v1");
        }

        public string Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return string.Empty;
            if (IsProtected(plaintext)) return plaintext;
            return Prefix + _protector.Protect(plaintext);
        }

        public string Unprotect(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (!IsProtected(value)) return value;
            var cipher = value.Substring(Prefix.Length);
            try
            {
                return _protector.Unprotect(cipher);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[SecretProtector] decrypt failed ({ex.GetType().Name}): {ex.Message}. Config file will read blank — re-enter the credential to re-seal.");
                return string.Empty;
            }
        }

        public static bool IsProtected(string? value) =>
            !string.IsNullOrEmpty(value) && value!.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
