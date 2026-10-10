using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    // GameFiles rows are relative to the game's folder, or to the folder a single-file game lies in; a file outside
    // that folder is stored by its full path
    [TestFixture]
    public class GameFilePathsTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retroarr_gamefilepaths_" + Guid.NewGuid().ToString("N"))).FullName;

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        private string Put(params string[] parts)
        {
            var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "data");
            return path;
        }

        private static string Stored(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

        [Test]
        public void FolderGame_FilesInsideAreRelative_OthersFull()
        {
            var game = Path.Combine(_root, "switch", "Zelda");
            var update = Put("switch", "Zelda", "Updates", "Zelda v1.nsp");
            var outside = Put("switch", "Updates", "Zelda v2.nsp");

            Assert.That(GameFilePaths.FilesFolder(game), Is.EqualTo(game));
            Assert.That(GameFilePaths.StoredPath(game, update), Is.EqualTo("Updates/Zelda v1.nsp"));
            Assert.That(GameFilePaths.StoredPath(game, outside), Is.EqualTo(Stored(outside)));
            Assert.That(GameFilePaths.Resolve(game, "Updates/Zelda v1.nsp"), Is.EqualTo(update));
            Assert.That(GameFilePaths.Resolve(game, Stored(outside)), Is.EqualTo(outside));
        }

        [Test]
        public void SingleFileGame_FilesAreRelativeToItsFolder()
        {
            var nsp = Put("switch", "Zelda.nsp");
            var update = Put("switch", "Updates", "Zelda v1.nsp");

            Assert.That(GameFilePaths.FilesFolder(nsp), Is.EqualTo(Path.Combine(_root, "switch")));
            Assert.That(GameFilePaths.StoredPath(nsp, update), Is.EqualTo("Updates/Zelda v1.nsp"));
            Assert.That(GameFilePaths.Resolve(nsp, "Updates/Zelda v1.nsp"), Is.EqualTo(update));
        }

        [Test]
        public void GameWithoutAPath_StoresFullPaths_AndCannotResolveRelativeOnes()
        {
            var update = Put("switch", "Updates", "Zelda v1.nsp");

            Assert.That(GameFilePaths.FilesFolder(null), Is.Null);
            Assert.That(GameFilePaths.StoredPath(null, update), Is.EqualTo(Stored(update)));
            Assert.That(GameFilePaths.Resolve(null, Stored(update)), Is.EqualTo(update));
            Assert.That(GameFilePaths.Resolve(string.Empty, "Updates/Zelda v1.nsp"), Is.Null);
        }
    }
}
