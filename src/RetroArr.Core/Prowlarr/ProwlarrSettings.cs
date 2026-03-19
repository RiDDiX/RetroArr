using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Prowlarr
{
    public class ProwlarrSettings
    {
        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        public string Url { get; set; } = "http://localhost:9696";
        public string ApiKey { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ApiKey);
        public bool IsConfiguredSetter { private get; set; } // Dummy to prevent issues if present in JSON
    }
}
