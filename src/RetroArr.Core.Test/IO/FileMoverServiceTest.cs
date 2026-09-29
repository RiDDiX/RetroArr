using System.IO;
using NUnit.Framework;
using RetroArr.Core.IO;

namespace RetroArr.Core.Test.IO
{
    [TestFixture]
    public class FileMoverServiceTest
    {
        private string _root = null!;
        private string _source = null!;
        private string _dest = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retroarr_mover_" + Path.GetRandomFileName())).FullName;
            _source = Path.Combine(_root, "source.gba");
            _dest = Path.Combine(_root, "library", "Game.gba");
            File.WriteAllText(_source, "new");
            Directory.CreateDirectory(Path.GetDirectoryName(_dest)!);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void ExistingFile_IsNeverReplaced()
        {
            File.WriteAllText(_dest, "old");

            var ok = new FileMoverService().ImportFile(_source, _dest, out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("not overwriting"));
            Assert.That(File.ReadAllText(_dest), Is.EqualTo("old"));
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void LinkToTheSource_IsReplacedByTheFile()
        {
            File.CreateSymbolicLink(_dest, _source);

            var ok = new FileMoverService().ImportFile(_source, _dest, out var reason);

            Assert.That(ok, Is.True, reason);
            Assert.That(new FileInfo(_dest).LinkTarget, Is.Null);
            Assert.That(File.ReadAllText(_dest), Is.EqualTo("new"));
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void LinkToAnotherFile_IsRefused()
        {
            var other = Path.Combine(_root, "other.gba");
            File.WriteAllText(other, "other");
            File.CreateSymbolicLink(_dest, other);

            var ok = new FileMoverService().ImportFile(_source, _dest, out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("not overwriting"));
            Assert.That(new FileInfo(_dest).LinkTarget, Is.EqualTo(other));
            Assert.That(File.ReadAllText(other), Is.EqualTo("other"));
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void DanglingLink_IsRefused()
        {
            File.CreateSymbolicLink(_dest, Path.Combine(_root, "unmounted", "Game.gba"));

            var ok = new FileMoverService().ImportFile(_source, _dest, out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("not overwriting"));
            Assert.That(new FileInfo(_dest).LinkTarget, Is.Not.Null);
        }
    }
}
