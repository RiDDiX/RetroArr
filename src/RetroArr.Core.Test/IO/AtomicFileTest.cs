using System.IO;
using NUnit.Framework;
using RetroArr.Core.IO;

namespace RetroArr.Core.Test.IO
{
    [TestFixture]
    public class AtomicFileTest
    {
        private string _dir = null!;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "retroarr_atomic_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void WriteAllText_LeavesContentAndNoTmpFile_AndSecondWriteWins()
        {
            var path = Path.Combine(_dir, "config.json");

            AtomicFile.WriteAllText(path, "first");
            Assert.That(File.ReadAllText(path), Is.EqualTo("first"));
            Assert.That(File.Exists(path + ".tmp"), Is.False);

            AtomicFile.WriteAllText(path, "second");
            Assert.That(File.ReadAllText(path), Is.EqualTo("second"));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }
    }
}
