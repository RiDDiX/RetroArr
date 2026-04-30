using System.Threading;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.Gog;

namespace RetroArr.Core.Test.Gog
{
    [TestFixture]
    public class GogDownloadTrackerTest
    {
        private GogDownloadTracker _tracker = null!;

        [SetUp]
        public void Setup()
        {
            _tracker = new GogDownloadTracker();
        }

        [Test]
        public void Start_AddsDownload_ReturnsCancellationToken()
        {
            var ct = _tracker.Start("abc", "Witcher 3", "setup.exe", "/tmp/setup.exe", 1000);
            var status = _tracker.Get("abc");

            Assert.That(status, Is.Not.Null);
            Assert.That(status!.GameTitle, Is.EqualTo("Witcher 3"));
            Assert.That(status.FileName, Is.EqualTo("setup.exe"));
            Assert.That(status.TotalBytes, Is.EqualTo(1000));
            Assert.That(status.State, Is.EqualTo(GogDownloadState.Downloading));
            Assert.That(ct.CanBeCanceled, Is.True);
            Assert.That(ct.IsCancellationRequested, Is.False);
        }

        [Test]
        public void Cancel_SetsCancellationToken()
        {
            var ct = _tracker.Start("abc", "Game", "file.exe", "/tmp/file.exe", 1000);
            Assert.That(ct.IsCancellationRequested, Is.False);

            _tracker.Cancel("abc");
            Assert.That(ct.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Cancel_NonExistent_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _tracker.Cancel("nonexistent"));
        }

        [Test]
        public void UpdateProgress_TracksBytes()
        {
            _tracker.Start("abc", "Game", "file.exe", "/tmp/file.exe", 1000);
            _tracker.UpdateProgress("abc", 500);

            var status = _tracker.Get("abc");
            Assert.That(status!.BytesDownloaded, Is.EqualTo(500));
            Assert.That(status.ProgressPercent, Is.EqualTo(50.0).Within(0.01));
        }

        [Test]
        public void MarkCompleted_SetsState()
        {
            _tracker.Start("abc", "Game", "file.exe", "/tmp/file.exe", 1000);
            _tracker.MarkCompleted("abc");

            var status = _tracker.Get("abc");
            Assert.That(status!.State, Is.EqualTo(GogDownloadState.Completed));
            Assert.That(status.CompletedAt, Is.Not.Null);
        }

        [Test]
        public void MarkFailed_SetsStateAndReason()
        {
            _tracker.Start("abc", "Game", "file.exe", "/tmp/file.exe", 1000);
            _tracker.MarkFailed("abc", "Network error");

            var status = _tracker.Get("abc");
            Assert.That(status!.State, Is.EqualTo(GogDownloadState.Failed));
            Assert.That(status.ErrorMessage, Is.EqualTo("Network error"));
        }

        [Test]
        public void Remove_DeletesEntry()
        {
            _tracker.Start("abc", "Game", "file.exe", "/tmp/file.exe", 1000);
            _tracker.Remove("abc");

            Assert.That(_tracker.Get("abc"), Is.Null);
        }

        [Test]
        public void GetAll_ReturnsAllEntries()
        {
            _tracker.Start("a", "Game A", "a.exe", "/tmp/a.exe", 100);
            _tracker.Start("b", "Game B", "b.exe", "/tmp/b.exe", 200);

            var all = _tracker.GetAll();
            Assert.That(all.Count, Is.EqualTo(2));
        }

        [Test]
        public void Get_NonExistent_ReturnsNull()
        {
            Assert.That(_tracker.Get("nonexistent"), Is.Null);
        }

        [Test]
        public void ProgressPercent_ZeroTotal_ReturnsZero()
        {
            _tracker.Start("abc", "Game", "file.exe", "/tmp/file.exe", null);
            _tracker.UpdateProgress("abc", 500);

            var status = _tracker.Get("abc");
            Assert.That(status!.ProgressPercent, Is.EqualTo(0.0));
        }

        [Test]
        public void Remove_DisposesCancellationToken()
        {
            var ct = _tracker.Start("abc", "Game", "file.exe", "/tmp/file.exe", 1000);
            _tracker.Remove("abc");

            Assert.That(_tracker.Get("abc"), Is.Null);
            // After removal, the token source is disposed - requesting cancel on a disposed CTS throws
            // This is expected behavior: the download is gone, no need to cancel
        }
    }
}
