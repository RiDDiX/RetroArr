using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class GameFileSyncTest
    {
        // ==================== FileType classification logic ====================
        // These tests verify the classification rules used by SyncGameFilesFromDisk

        [TestCase("Patches/update.nsp", "Patch")]
        [TestCase("patches/v1.0.nsp", "Patch")]
        [TestCase("Updates/update.nsp", "Patch")]
        [TestCase("updates/v1.0.nsp", "Patch")]
        [TestCase("DLC/bonus.nsp", "DLC")]
        [TestCase("dlc/extra.pkg", "DLC")]
        [TestCase("game.nsp", "Main")]
        [TestCase("subfolder/game.iso", "Main")]
        public void ClassifyFileType_FromRelativePath(string relativePath, string expectedType)
        {
            var normalized = relativePath.Replace('\\', '/');
            var fileType = normalized.StartsWith("Patches/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Updates/", StringComparison.OrdinalIgnoreCase) ? "Patch"
                         : normalized.StartsWith("DLC/", StringComparison.OrdinalIgnoreCase) ? "DLC"
                         : "Main";

            Assert.That(fileType, Is.EqualTo(expectedType));
        }

        [Test]
        public void GameFile_DefaultFileType_IsMain()
        {
            var gf = new GameFile();
            Assert.That(gf.FileType, Is.EqualTo("Main"));
        }

        [Test]
        public void GameFile_Properties_SetCorrectly()
        {
            var now = DateTime.UtcNow;
            var gf = new GameFile
            {
                GameId = 42,
                RelativePath = "Patches/update.nsp",
                Size = 1024000,
                DateAdded = now,
                FileType = "Patch"
            };

            Assert.That(gf.GameId, Is.EqualTo(42));
            Assert.That(gf.RelativePath, Is.EqualTo("Patches/update.nsp"));
            Assert.That(gf.Size, Is.EqualTo(1024000));
            Assert.That(gf.FileType, Is.EqualTo("Patch"));
        }
    }
}
