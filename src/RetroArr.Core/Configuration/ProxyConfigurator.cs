using System;
using System.Net;

namespace RetroArr.Core.Configuration
{
    // One stable IWebProxy assigned to HttpClient.DefaultProxy at startup. Every
    // RetroArr HttpClient leaves UseProxy=true/Proxy=null, so all of them honor it.
    // The snapshot swaps atomically, so a settings save takes effect without a
    // restart, even for long-lived singleton clients holding this instance.
    public sealed class ProxyConfigurator : IWebProxy
    {
        private static readonly NLog.Logger _logger =
            NLog.LogManager.GetLogger(Logging.AppLoggerService.Configuration);

        public static ProxyConfigurator Instance { get; } = new ProxyConfigurator();

        private volatile Snapshot _current =
            new Snapshot(false, null, null, Array.Empty<string>(), false);

        private ProxyConfigurator() { }

        public ICredentials? Credentials
        {
            get => _current.Credentials;
            set { /* managed via Update, setter required by IWebProxy */ }
        }

        public void Update(ProxySettings? settings)
        {
            if (settings == null || !settings.Enabled || string.IsNullOrWhiteSpace(settings.Host))
            {
                _current = new Snapshot(false, null, null, Array.Empty<string>(), false);
                _logger.Info("[Proxy] Disabled, outgoing traffic goes direct.");
                return;
            }

            var scheme = string.Equals(settings.Type, "socks5", StringComparison.OrdinalIgnoreCase)
                ? "socks5" : "http";

            Uri uri;
            try
            {
                uri = new Uri($"{scheme}://{settings.Host}:{settings.Port}");
            }
            catch (UriFormatException ex)
            {
                _logger.Error($"[Proxy] Invalid host/port, keeping previous setting: {ex.Message}");
                return;
            }

            ICredentials? creds = string.IsNullOrWhiteSpace(settings.Username)
                ? null
                : new NetworkCredential(settings.Username, settings.Password);

            var bypass = settings.BypassList != null
                ? settings.BypassList.ToArray()
                : Array.Empty<string>();

            _current = new Snapshot(true, uri, creds, bypass, settings.BypassLocal);
            _logger.Info($"[Proxy] Enabled {scheme}://{settings.Host}:{settings.Port} (bypassLocal={settings.BypassLocal}).");
        }

        public Uri? GetProxy(Uri destination)
        {
            var snap = _current;
            if (!snap.Enabled || snap.Uri == null) return destination;
            return IsBypassed(destination) ? destination : snap.Uri;
        }

        public bool IsBypassed(Uri host)
        {
            var snap = _current;
            if (!snap.Enabled) return true;
            if (host == null) return false;
            if (snap.BypassLocal && host.IsLoopback) return true;
            foreach (var entry in snap.Bypass)
            {
                if (!string.IsNullOrWhiteSpace(entry) &&
                    host.Host.IndexOf(entry, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private sealed class Snapshot
        {
            public Snapshot(bool enabled, Uri? uri, ICredentials? creds, string[] bypass, bool bypassLocal)
            {
                Enabled = enabled;
                Uri = uri;
                Credentials = creds;
                Bypass = bypass;
                BypassLocal = bypassLocal;
            }
            public bool Enabled { get; }
            public Uri? Uri { get; }
            public ICredentials? Credentials { get; }
            public string[] Bypass { get; }
            public bool BypassLocal { get; }
        }
    }
}
