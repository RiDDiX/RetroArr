using System.IO;
using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class FileSetResolverTest
    {
        private string _tempDir = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "retroarr_test_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Test]
        public void Resolve_SingleFile_ReturnsSingleType()
        {
            var romPath = Path.Combine(_tempDir, "game.z64");
            File.WriteAllText(romPath, "dummy");

            var set = FileSetResolver.Resolve(romPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.Single));
            Assert.That(set.PrimaryFile, Is.EqualTo(romPath));
            Assert.That(set.CompanionFiles, Is.Empty);
        }

        [Test]
        public void Resolve_CueBin_FindsReferencedBinFiles()
        {
            var binPath = Path.Combine(_tempDir, "Track 01.bin");
            File.WriteAllText(binPath, "dummy bin data");

            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cuePath, "FILE \"Track 01.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");

            var set = FileSetResolver.Resolve(cuePath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.CueBin));
            Assert.That(set.PrimaryFile, Is.EqualTo(cuePath));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]), Is.EqualTo("Track 01.bin"));
        }

        [Test]
        public void Resolve_CueBin_MultipleBinFiles()
        {
            var bin1 = Path.Combine(_tempDir, "Game (Track 1).bin");
            var bin2 = Path.Combine(_tempDir, "Game (Track 2).bin");
            File.WriteAllText(bin1, "data1");
            File.WriteAllText(bin2, "data2");

            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cuePath,
                "FILE \"Game (Track 1).bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n" +
                "FILE \"Game (Track 2).bin\" BINARY\n  TRACK 02 AUDIO\n    INDEX 01 00:00:00\n");

            var set = FileSetResolver.Resolve(cuePath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.CueBin));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_M3U_FindsReferencedDiscFiles()
        {
            var cue1 = Path.Combine(_tempDir, "Disc1.cue");
            var cue2 = Path.Combine(_tempDir, "Disc2.cue");
            File.WriteAllText(cue1, "FILE \"Disc1.bin\" BINARY\n");
            File.WriteAllText(cue2, "FILE \"Disc2.bin\" BINARY\n");

            var m3uPath = Path.Combine(_tempDir, "Game.m3u");
            File.WriteAllText(m3uPath, "Disc1.cue\nDisc2.cue\n");

            var set = FileSetResolver.Resolve(m3uPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.M3U));
            Assert.That(set.PrimaryFile, Is.EqualTo(m3uPath));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_M3U_SkipsComments()
        {
            var disc = Path.Combine(_tempDir, "Disc1.cue");
            File.WriteAllText(disc, "FILE \"Disc1.bin\" BINARY\n");

            var m3uPath = Path.Combine(_tempDir, "Game.m3u");
            File.WriteAllText(m3uPath, "# This is a comment\nDisc1.cue\n\n");

            var set = FileSetResolver.Resolve(m3uPath);

            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_GDI_FindsTrackFiles()
        {
            var track1 = Path.Combine(_tempDir, "track01.bin");
            var track2 = Path.Combine(_tempDir, "track02.raw");
            File.WriteAllText(track1, "data");
            File.WriteAllText(track2, "data");

            var gdiPath = Path.Combine(_tempDir, "disc.gdi");
            File.WriteAllText(gdiPath,
                "2\n" +
                "1 0 4 2352 track01.bin 0\n" +
                "2 600 0 2352 track02.raw 0\n");

            var set = FileSetResolver.Resolve(gdiPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.GDI));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_SingleWithCompanion_FindsSbiFile()
        {
            var binPath = Path.Combine(_tempDir, "Game.bin");
            var sbiPath = Path.Combine(_tempDir, "Game.sbi");
            File.WriteAllText(binPath, "data");
            File.WriteAllText(sbiPath, "sbi data");

            var set = FileSetResolver.Resolve(binPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.Single));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]), Is.EqualTo("Game.sbi"));
        }

        [Test]
        public void ResolveDirectory_GroupsFileSets()
        {
            var bin1 = Path.Combine(_tempDir, "Track 01.bin");
            File.WriteAllText(bin1, "data");
            var cue = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cue, "FILE \"Track 01.bin\" BINARY\n");
            var standalone = Path.Combine(_tempDir, "Other.iso");
            File.WriteAllText(standalone, "data");

            var sets = FileSetResolver.ResolveDirectory(_tempDir);

            Assert.That(sets.Count, Is.EqualTo(2));
            var cueSet = sets.First(s => s.Type == FileSetType.CueBin);
            Assert.That(cueSet.CompanionFiles.Count, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_EmptyPath_ReturnsSingle()
        {
            var set = FileSetResolver.Resolve(string.Empty);
            Assert.That(set.Type, Is.EqualTo(FileSetType.Single));
        }
    }
}
