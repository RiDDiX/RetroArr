using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class ReviewItemServiceTest
    {
        private string _root = null!;
        private ConfigurationService _config = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_reviewitems_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _config = new ConfigurationService(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static ReviewItem File(string path) => new() { FilePaths = { path }, DetectedTitle = Path.GetFileNameWithoutExtension(path) };

        // The list offers Finalize for a mapped item, so it stays on it, and the next scan finds it instead of adding the file again
        [Test]
        public void MappedItem_StaysOpenUntilFinalized()
        {
            var reviews = new ReviewItemService(_config);
            var item = reviews.Add(File("/roms/Chrono Trigger.sfc"));
            reviews.UpdateMapping(item.Id, 41, null, null, null);

            Assert.That(reviews.GetPending().Select(i => (i.Id, i.Status)), Is.EqualTo(new[] { (item.Id, ReviewStatus.Mapped) }));
            Assert.That(reviews.Add(File("/ROMS/chrono trigger.sfc")), Is.SameAs(item));

            reviews.SetStatus(item.Id, ReviewStatus.Finalized);
            Assert.That(reviews.GetPending(), Is.Empty);
            Assert.That(reviews.Add(File("/roms/Chrono Trigger.sfc")).Id, Is.Not.EqualTo(item.Id));
        }

        // An ignored file stays ignored: the next scan gets the Ignored item back, adds no row and the open list stays empty
        [Test]
        public void IgnoredItem_IsNotAddedAgain_AndNotListedAsOpen()
        {
            var reviews = new ReviewItemService(_config);
            var item = reviews.Add(File("/roms/Skip Me.sfc"));
            reviews.SetStatus(item.Id, ReviewStatus.Ignored);

            var again = reviews.Add(File("/roms/Skip Me.sfc"));

            Assert.That(again, Is.SameAs(item));
            Assert.That(again.Status, Is.EqualTo(ReviewStatus.Ignored));
            Assert.That(reviews.GetAll(), Has.Count.EqualTo(1));
            Assert.That(reviews.GetPending(), Is.Empty);
        }

        // A scan adds them faster than one write a second; none is lost by shutdown, and each file stays one item
        [Test]
        public void ItemsAddedInABurst_AreAllStoredOnShutdown()
        {
            var reviews = new ReviewItemService(_config);
            var added = Enumerable.Range(1, 50).Select(n => reviews.Add(File($"/roms/Game {n}.sfc"))).ToList();
            Assert.That(reviews.Add(File("/roms/Game 7.sfc")), Is.SameAs(added[6]));
            reviews.Dispose();

            var reloaded = new ReviewItemService(_config);
            Assert.That(reloaded.GetPending().Select(i => i.Id), Is.EqualTo(added.Select(i => i.Id)));
            Assert.That(reloaded.Add(File("/roms/Game 50.sfc")).Id, Is.EqualTo(added[49].Id));
        }

        // The headless host (Docker) never disposes its services: the held back items still reach the store
        [Test]
        public void ItemsAddedInABurst_AreStoredWithoutShutdown()
        {
            var reviews = new ReviewItemService(_config);
            var added = Enumerable.Range(1, 50).Select(n => reviews.Add(File($"/roms/Game {n}.sfc"))).ToList();

            var stored = new ReviewItemService(_config).GetPending();
            for (var waited = 0; stored.Count < added.Count && waited < 5000; waited += 100)
            {
                System.Threading.Thread.Sleep(100);
                stored = new ReviewItemService(_config).GetPending();
            }
            Assert.That(stored.Select(i => i.Id), Is.EqualTo(added.Select(i => i.Id)));
            GC.KeepAlive(reviews);
        }
    }
}
