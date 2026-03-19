using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RetroArr.Core.Plugins
{
    public class PluginManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = "1";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "script"; // "script" or "metadata"

        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("args")]
        public string Args { get; set; } = string.Empty;

        [JsonPropertyName("permissions")]
        public List<string> Permissions { get; set; } = new();

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 10;
    }

    public static class PluginApiVersions
    {
        public const string Current = "1";
        public static readonly string[] Supported = { "1" };
    }

    public static class PluginTypes
    {
        public const string Script = "script";
        public const string Metadata = "metadata";
        public static readonly string[] All = { Script, Metadata };
    }

    public static class PluginPermissionKeys
    {
        public const string FilesystemRead = "filesystem:read";
        public const string FilesystemWrite = "filesystem:write";
        public const string NetworkHttp = "network:http";

        public static readonly string[] All = { FilesystemRead, FilesystemWrite, NetworkHttp };
    }
}
