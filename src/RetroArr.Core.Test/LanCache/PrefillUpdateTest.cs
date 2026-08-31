using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;
using RetroArr.Core.LanCache;

namespace RetroArr.Core.Test.LanCache
{
    // Updating a prefill tool means picking the right release asset and finding the
    // binary inside the zip. Both are silent-failure spots: a wrong asset installs
    // the wrong architecture, a missed binary leaves the provider "not bundled".
    [TestFixture]
    public class PrefillUpdateTest
    {
        // Shape of https://api.github.com/repos/tpill90/steam-lancache-prefill/releases/latest
        private const string ReleaseJson = @"{
          ""tag_name"": ""v3.7.2"",
          ""assets"": [
            { ""name"": ""SteamPrefill-3.7.2-linux-arm64.zip"", ""browser_download_url"": ""https://x/arm64.zip"" },
            { ""name"": ""SteamPrefill-3.7.2-linux-x64.zip"",   ""browser_download_url"": ""https://x/x64.zip"" },
            { ""name"": ""SteamPrefill-3.7.2-win-x64.zip"",     ""browser_download_url"": ""https://x/win.zip"" }
          ]
        }";

        private static JsonElement Release(string json = ReleaseJson) => JsonDocument.Parse(json).RootElement;

        [TestCase("v3.7.2", "3.7.2")]
        [TestCase("V3.7.2", "3.7.2")]
        [TestCase("3.7.2", "3.7.2")]
        [TestCase("  v3.7.2\r", "3.7.2")]
        [TestCase(null, "")]
        [TestCase("", "")]
        public void NormalizeVersion_StripsTagPrefix(string? raw, string expected)
        {
            Assert.That(LanCachePrefillService.NormalizeVersion(raw), Is.EqualTo(expected));
        }

        [Test]
        public void PickAssetUrl_MatchesArchitecture()
        {
            Assert.That(LanCachePrefillService.PickAssetUrl(Release(), Architecture.X64), Is.EqualTo("https://x/x64.zip"));
            Assert.That(LanCachePrefillService.PickAssetUrl(Release(), Architecture.Arm64), Is.EqualTo("https://x/arm64.zip"));
        }

        [Test]
        public void PickAssetUrl_Null_WhenNothingFits()
        {
            // No linux build for this arch, and a release without assets at all.
            Assert.That(LanCachePrefillService.PickAssetUrl(Release(), Architecture.X86), Is.Null);
            Assert.That(LanCachePrefillService.PickAssetUrl(Release(@"{""tag_name"":""v1""}"), Architecture.X64), Is.Null);
        }

        [Test]
        public void FindBinary_FindsItNestedAndAtTheRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                // The zips ship the binary one directory down.
                var nested = Path.Combine(root, "SteamPrefill-3.7.2-linux-x64");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(nested, "update.sh"), "#!/bin/bash");
                File.WriteAllText(Path.Combine(nested, "SteamPrefill"), "binary");
                Assert.That(LanCachePrefillService.FindBinary(root, "SteamPrefill"), Is.EqualTo(Path.Combine(nested, "SteamPrefill")));

                Assert.That(LanCachePrefillService.FindBinary(root, "EpicPrefill"), Is.Null);

                var flat = Path.Combine(root, "flat");
                Directory.CreateDirectory(flat);
                File.WriteAllText(Path.Combine(flat, "EpicPrefill"), "binary");
                Assert.That(LanCachePrefillService.FindBinary(flat, "EpicPrefill"), Is.EqualTo(Path.Combine(flat, "EpicPrefill")));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* temp dir */ }
            }
        }
    }
}
