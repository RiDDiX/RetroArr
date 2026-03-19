using System;
using System.IO;
using NUnit.Framework;
using RetroArr.Core.Download;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class DownloadPlatformTrackerTest
    {
        private string _tempDir = null!;
        private DownloadPlatformTracker _tracker = null!;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"retroarr_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _tracker = new DownloadPlatformTracker(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public void SetAndLookup_ReturnsPlatform()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            Assert.That(_tracker.LookupByName("MyGame.nsp"), Is.EqualTo("switch"));
        }

        [Test]
        public void SetAndLookup_CaseInsensitive()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            Assert.That(_tracker.LookupByName("mygame.nsp"), Is.EqualTo("switch"));
        }

        [Test]
        public void LookupByName_NotFound_ReturnsNull()
        {
            Assert.That(_tracker.LookupByName("nonexistent"), Is.Null);
        }

        [Test]
        public void SetPlatform_WithGameId_CanLookupGameId()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch", gameId: 42);
            Assert.That(_tracker.LookupGameId("MyGame.nsp"), Is.EqualTo(42));
        }

        [Test]
        public void SetPlatform_WithImportSubfolder_CanLookupSubfolder()
        {
            _tracker.SetPlatformForDownload("MyUpdate.nsp", "switch", importSubfolder: "Patches");
            Assert.That(_tracker.LookupImportSubfolder("MyUpdate.nsp"), Is.EqualTo("Patches"));
        }

        [Test]
        public void SetPlatform_Overwrites_PreviousEntry()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            _tracker.SetPlatformForDownload("MyGame.nsp", "ps4");
            Assert.That(_tracker.LookupByName("MyGame.nsp"), Is.EqualTo("ps4"));
        }

        [Test]
        public void MarkProcessed_RemovesEntry()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            _tracker.MarkProcessed("MyGame.nsp");
            Assert.That(_tracker.LookupByName("MyGame.nsp"), Is.Null);
        }

        [Test]
        public void Persistence_SurvivesReload()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch", gameId: 7, importSubfolder: "DLC");

            // Create a new tracker from the same directory
            var tracker2 = new DownloadPlatformTracker(_tempDir);
            Assert.That(tracker2.LookupByName("MyGame.nsp"), Is.EqualTo("switch"));
            Assert.That(tracker2.LookupGameId("MyGame.nsp"), Is.EqualTo(7));
            Assert.That(tracker2.LookupImportSubfolder("MyGame.nsp"), Is.EqualTo("DLC"));
        }
    }
}
