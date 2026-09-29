using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RetroArr.Api.V3.Settings;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Configuration
{
    [TestFixture]
    public class MediaSettingsSaveTest
    {
        [Test]
        public void PartialSave_KeepsOtherFields()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_mediasave_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "config"));
            try
            {
                var config = new ConfigurationService(root);
                var library = Path.Combine(root, "library");
                config.SaveMediaSettings(new MediaSettings
                {
                    FolderPath = library, MissingRetentionDays = 0,
                    RenameOnImport = false, MainFileTemplate = "{Title} ({Region})", FileConflictBehavior = "Suffix"
                });
                var controller = new MediaController(config, null!, null!, null!, null!);

                // what the Rename tab sends
                controller.SaveSettings(JObject.Parse("{\"renameOnImport\": true}"));
                var afterRename = config.LoadMediaSettings();
                Assert.That(afterRename.RenameOnImport, Is.True);
                Assert.That(afterRename.FolderPath, Is.EqualTo(library));
                Assert.That(afterRename.MissingRetentionDays, Is.EqualTo(0));

                // what the Media tab sends
                var moved = Path.Combine(root, "library2");
                controller.SaveSettings(JObject.Parse($"{{\"FolderPath\": {JToken.FromObject(moved).ToString(Newtonsoft.Json.Formatting.None)}}}"));
                var afterMedia = config.LoadMediaSettings();
                Assert.That(afterMedia.FolderPath, Is.EqualTo(moved));
                Assert.That(afterMedia.RenameOnImport, Is.True);
                Assert.That(afterMedia.MainFileTemplate, Is.EqualTo("{Title} ({Region})"));
                Assert.That(afterMedia.FileConflictBehavior, Is.EqualTo("Suffix"));
                Assert.That(afterMedia.MissingRetentionDays, Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
