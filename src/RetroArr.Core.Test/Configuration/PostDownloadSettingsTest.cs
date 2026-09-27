using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Configuration
{
    [TestFixture]
    public class PostDownloadSettingsTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_pdsettings_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        // Binds the body the way POST /api/v3/postdownload does (Newtonsoft, settings from Program.cs).
        private static PostDownloadSettings Bind(string json) =>
            JsonConvert.DeserializeObject<PostDownloadSettings>(json, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            })!;

        [TestCase("[\".txt\",\".nfo\",\".url\"]", new[] { ".txt", ".nfo", ".url" })]
        [TestCase("[\".nfo\",\".sfv\"]", new[] { ".nfo", ".sfv" })]
        [TestCase("[]", new string[0])]
        public void PostedList_ReplacesDefaults_AndRoundTrips(string list, string[] expected)
        {
            var config = new ConfigurationService(_root);
            config.SavePostDownloadSettings(Bind("{\"enableDeepClean\":true,\"unwantedExtensions\":" + list + "}"));

            Assert.That(config.LoadPostDownloadSettings().UnwantedExtensions, Is.EqualTo(expected));
        }

        [Test]
        public void Load_DropsDuplicatesAndBlanks_KeepsOrder()
        {
            File.WriteAllText(Path.Combine(_root, "config", "postdownload.json"),
                "{\"EnableDeepClean\":false,\"UnwantedExtensions\":[\".txt\",\".nfo\",\".url\",\".sfv\",\".txt\",\"\",\".nfo\",\".url\",\" \"]}");

            var loaded = new ConfigurationService(_root).LoadPostDownloadSettings();

            Assert.That(loaded.UnwantedExtensions, Is.EqualTo(new[] { ".txt", ".nfo", ".url", ".sfv" }));
            Assert.That(loaded.EnableDeepClean, Is.False);
        }
    }
}
